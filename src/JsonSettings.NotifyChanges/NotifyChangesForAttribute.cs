using System;

namespace Nucs.JsonSettings.NotifyChanges {
    /// <summary>
    ///     When the annotated property changes and raises its notification, also raise one for each of
    ///     the named <paramref name="otherProperties"/>. Use it to keep a <em>computed</em> property's
    ///     bindings live: mark the input property with <c>[NotifyChangesFor(nameof(TheComputedOne))]</c>
    ///     and a write to the input refreshes the computed one too.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The counterpart to CommunityToolkit.Mvvm's <c>[NotifyPropertyChangedFor]</c>. It attaches to
    ///     the <em>source</em> property (the one that is written), and lists the <em>dependent</em>
    ///     property names to fan the notification out to. The dependents are usually get-only computed
    ///     properties that have no setter of their own to weave.
    ///     </para>
    ///     <para>
    ///     Dependents fire <em>unconditionally</em> whenever the source actually raised -- they are
    ///     derived, so the source's <see cref="NotificationGuard"/> is the only gate. A no-op write the
    ///     guard suppresses fans out nothing. Targets that cannot carry a notification are dropped
    ///     silently: a self-reference, an empty name, an indexer, the framework's
    ///     <c>FileName</c>/<c>Modulation</c>/<c>Version</c>, and any property marked
    ///     <see cref="IgnoreNotifyAttribute"/>. Names are matched exactly, so prefer
    ///     <c>nameof(...)</c> over string literals.
    ///     </para>
    ///     <para>
    ///     Repeatable, so several may stack on one property; their targets are merged and de-duplicated
    ///     while preserving declared order. Works the same under <see cref="NotifyChangesAttribute"/>
    ///     (raised through the class's raiser) and <see cref="NotifyChangesMixinAttribute"/> (raised on
    ///     the injected event).
    ///     </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [Autosave, NotifyChanges]
    /// public class ProfileSettings : NotifiyingJsonSettings {
    ///     public override string FileName { get; set; } = "profile.json";
    ///
    ///     [NotifyChangesFor(nameof(FullName))]
    ///     public string First { get; set; }
    ///     [NotifyChangesFor(nameof(FullName))]
    ///     public string Last  { get; set; }
    ///
    ///     [JsonIgnore]                                   // computed, not persisted, no setter to weave
    ///     public string FullName => $"{First} {Last}";   // its binding refreshes when First/Last change
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public sealed class NotifyChangesForAttribute : Attribute {
        /// <summary>
        ///     The names of the other properties to raise a change notification for when the annotated
        ///     property changes.
        /// </summary>
        public string[] OtherProperties { get; }

        public NotifyChangesForAttribute(params string[] otherProperties) {
            OtherProperties = otherProperties ?? Array.Empty<string>();
        }
    }
}
