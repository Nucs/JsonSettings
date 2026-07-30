using System;
using AspectInjector.Broker;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     Raises a change notification from every instance setter in the annotated scope, so a
    ///     settings class binds to WPF (or any <see cref="System.ComponentModel.INotifyPropertyChanged"/>
    ///     consumer) without a hand-written <c>OnPropertyChanged()</c> in each setter.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This is the advice-only layer: it produces the <em>notification</em> but does not provide
    ///     the event. The class must already expose a raiser -- the shipped
    ///     <see cref="Nucs.JsonSettings.Examples.NotifiyingJsonSettings"/> base is the intended one,
    ///     and common MVVM bases are recognised by convention (see
    ///     <see cref="NotifyChangesRuntime.RaiseViaBaseOrConvention"/>). For a standalone class that
    ///     implements nothing, use <see cref="NotifyChangesMixinAttribute"/>, which injects the
    ///     interface as well.
    ///     </para>
    ///     <para>
    ///     Like <see cref="AutosaveAttribute"/> this is a compile-time aspect woven by AspectInjector,
    ///     it emits no runtime code (Native-AOT-safe), and it is <em>not inherited</em>: a setter is
    ///     woven where it is declared, so every class in a hierarchy that declares properties you want
    ///     to notify on needs its own <c>[NotifyChanges]</c>. It composes with <c>[Autosave]</c> --
    ///     mark a class with both to save and notify from the same setters.
    ///     </para>
    ///     <para>
    ///     The advice is <see cref="Kind.Around"/> rather than <see cref="Kind.After"/> so the default
    ///     <see cref="NotificationGuard.OnlyChanged"/> guard can read the property's previous value
    ///     before the assignment and suppress a notification when nothing actually changed. Put
    ///     <c>[NotifyChanges]</c> on auto-properties: a hand-written setter that already calls
    ///     <c>OnPropertyChanged()</c> would then raise twice.
    ///     </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [Autosave]                       // save on change
    /// [NotifyChanges]                  // and notify WPF on change
    /// public class MySettings : NotifiyingJsonSettings {
    ///     public override string FileName { get; set; } = "config.json";
    ///     public string Name  { get; set; }   // auto-property: binds and saves, no boilerplate
    ///     [NotifyChanges(Guard = NotificationGuard.Always)]
    ///     public int Ticks { get; set; }      // per-property override: notify on every write
    /// }
    /// </code>
    /// </example>
    [Aspect(Scope.Global)]
    [Injection(typeof(NotifyChangesAttribute))]
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class NotifyChangesAttribute : Attribute, IHasNotificationGuard {
        /// <summary>
        ///     When a setter in this scope is allowed to raise a notification. Defaults to
        ///     <see cref="NotificationGuard.OnlyChanged"/>. A <c>[NotifyChanges]</c> on an individual
        ///     property overrides the one on its class.
        /// </summary>
        public NotificationGuard Guard { get; set; } = NotificationGuard.OnlyChanged;

        /// <summary>
        ///     Wraps every instance setter in the annotated scope. Reads the guard and old value
        ///     before the assignment, runs the assignment, then raises a notification if the guard
        ///     agrees this was a real change.
        /// </summary>
        [Advice(Kind.Around, Targets = Target.Setter | Target.AnyAccess | Target.Instance)]
        public object AroundSetter([Argument(Source.Instance)] object instance,
                                   [Argument(Source.Name)] string propertyName,
                                   [Argument(Source.Arguments)] object[] arguments,
                                   [Argument(Source.Target)] Func<object[], object> target) {
            if (NotifyChangesRuntime.IsNotifyIgnored(instance.GetType(), propertyName))
                return target(arguments);
            var decision = NotifyChangesRuntime.Prepare(instance, propertyName);
            var result = target(arguments);
            if (NotifyChangesRuntime.ShouldRaise(decision, arguments))
                NotifyChangesRuntime.RaiseViaBaseOrConvention(instance, propertyName);
            return result;
        }
    }
}
