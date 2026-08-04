#nullable enable
using System;
using System.ComponentModel;
using AspectInjector.Broker;

namespace Nucs.JsonSettings.NotifyChanges {
    /// <summary>
    ///     Makes a settings class implement <see cref="INotifyPropertyChanged"/> <em>and</em> raise it
    ///     from every instance setter -- a full WPF-bindable ViewModel with no base class and no
    ///     boilerplate. This is the opt-in mixin layer that sits on top of the advice-only
    ///     <see cref="NotifyChangesAttribute"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Where <see cref="NotifyChangesAttribute"/> requires the class to already own a
    ///     <c>PropertyChanged</c> event (via <see cref="Nucs.JsonSettings.Examples.NotifiyingJsonSettings"/>
    ///     or an MVVM base), this attribute uses AspectInjector's <see cref="Mixin"/> to <em>inject</em>
    ///     the <see cref="INotifyPropertyChanged"/> implementation into the annotated class at compile
    ///     time, then raises that injected event from the woven setters. The class does not declare the
    ///     interface in source; after the build it implements it, so <c>settings is INotifyPropertyChanged</c>
    ///     is true and WPF binds to it directly.
    ///     </para>
    ///     <para>
    ///     The aspect is <see cref="Scope.PerInstance"/>: each settings object gets its own aspect
    ///     instance holding its own event and subscriber list, which is what an event mixed into an
    ///     ordinary object requires (a <see cref="Scope.Global"/> singleton would share one subscriber
    ///     list across every instance).
    ///     </para>
    ///     <para>
    ///     Intended for a single settings class, including a <c>sealed</c> one. For an inheritance
    ///     hierarchy prefer <see cref="Nucs.JsonSettings.Examples.NotifiyingJsonSettings"/> +
    ///     <see cref="NotifyChangesAttribute"/> on each declaring class: because the interface can only
    ///     be mixed in once, a derived class cannot re-inject it, and a derived setter woven by the
    ///     advice-only attribute cannot reach the base's injected event. Do not combine this with a
    ///     class that already implements <see cref="INotifyPropertyChanged"/> (including
    ///     <c>NotifiyingJsonSettings</c>) -- that is a duplicate implementation of the same interface.
    ///     </para>
    ///     <para>
    ///     Note the boundary with autosave's nested-collection support: <c>EnableAutosave()</c> only
    ///     attaches a <c>NotificationBinder</c> to a <c>NotifiyingJsonSettings</c>, so a
    ///     mixin-only class still autosaves on its own property writes but not when a nested
    ///     <c>ObservableCollection</c> is mutated in place. Use the notifying base if you need that.
    ///     </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [Autosave]
    /// [NotifyChangesMixin]                       // no base class needed
    /// public sealed class MySettings : JsonSettings {
    ///     public override string FileName { get; set; } = "config.json";
    ///     public string Name { get; set; }
    /// }
    ///
    /// var settings = JsonSettings.Load&lt;MySettings&gt;("config.json").EnableAutosave();
    /// ((INotifyPropertyChanged) settings).PropertyChanged += (_, e) =&gt; { /* e.PropertyName == "Name" */ };
    /// settings.Name = "changed";                 // saved and notified
    /// </code>
    /// </example>
    [Aspect(Scope.PerInstance)]
    [Injection(typeof(NotifyChangesMixinAttribute))]
    [Mixin(typeof(INotifyPropertyChanged))]
    [Mixin(typeof(INotifyPropertyChanging))]
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class NotifyChangesMixinAttribute : Attribute, INotifyPropertyChanged, INotifyPropertyChanging, IHasNotificationGuard {
        /// <summary>
        ///     When a setter in this class is allowed to raise a notification. Defaults to
        ///     <see cref="NotificationGuard.OnlyChanged"/>. A <see cref="NotifyChangesAttribute"/> on
        ///     an individual property overrides this for that property.
        /// </summary>
        public NotificationGuard Guard { get; set; } = NotificationGuard.OnlyChanged;

        /// <summary>
        ///     The event injected into every annotated class. Subscribers added through the class's
        ///     mixed-in <see cref="INotifyPropertyChanged.PropertyChanged"/> are forwarded to the
        ///     per-instance aspect's copy of this event, which the woven setters raise.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        ///     The <see cref="INotifyPropertyChanging"/> event injected alongside
        ///     <see cref="PropertyChanged"/>, raised before the assignment so a mixin class is
        ///     symmetric with the notifying base. WPF binds on <c>PropertyChanged</c>; this is for
        ///     change trackers and validators that observe the "about to change" edge.
        /// </summary>
        public event PropertyChangingEventHandler? PropertyChanging;

        /// <summary>
        ///     Wraps every instance setter in the class. Shares the guard evaluation with
        ///     <see cref="NotifyChangesAttribute"/>; only the raise differs -- here it invokes the
        ///     events injected into this very instance. Raises <c>PropertyChanging</c> before the
        ///     assignment and <c>PropertyChanged</c> (plus any <see cref="NotifyChangesForAttribute"/>
        ///     dependents, marshalled if a context was captured) after it.
        /// </summary>
        [Advice(Kind.Around, Targets = Target.Setter | Target.AnyAccess | Target.Instance)]
        public object AroundSetter([Argument(Source.Instance)] object instance,
                                   [Argument(Source.Name)] string propertyName,
                                   [Argument(Source.Arguments)] object[] arguments,
                                   [Argument(Source.Target)] Func<object[], object> target) {
            if (NotifyChangesRuntime.IsNotifyIgnored(instance.GetType(), propertyName))
                return target(arguments);
            var decision = NotifyChangesRuntime.Prepare(instance, propertyName);
            var raise = NotifyChangesRuntime.ShouldRaise(decision, arguments);
            if (raise)
                PropertyChanging?.Invoke(instance, new PropertyChangingEventArgs(propertyName));
            var result = target(arguments);
            if (raise)
                NotifyChangesRuntime.RaiseChangedAndDependents(instance, propertyName, RaiseChanged);
            return result;
        }

        //The Action<object,string> handed to the shared runtime so dependents raise the same way as
        //the source: through THIS per-instance aspect's injected PropertyChanged.
        private void RaiseChanged(object instance, string propertyName) {
            PropertyChanged?.Invoke(instance, new PropertyChangedEventArgs(propertyName));
        }
    }
}
