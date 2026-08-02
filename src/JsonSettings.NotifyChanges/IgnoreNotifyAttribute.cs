using System;

namespace Nucs.JsonSettings.NotifyChanges {
    /// <summary>
    ///     The property's setter will not raise a change notification under
    ///     <see cref="NotifyChangesAttribute"/> or <see cref="NotifyChangesMixinAttribute"/>.
    /// </summary>
    /// <remarks>
    ///     The notification opt-out, and the counterpart to <see cref="IgnoreAutosaveAttribute"/> for
    ///     the *save* side. The two are independent: a property may save without notifying
    ///     (<c>[IgnoreNotify]</c>), notify without saving (<c>[IgnoreAutosave]</c>, or
    ///     <c>[JsonIgnore]</c>), both, or neither. That independence is deliberate &mdash; a UI-only
    ///     value that is never persisted can still drive a binding, and a persisted value can be kept
    ///     out of the notification stream.
    ///
    ///     Use it to silence a property that a class-level <c>[NotifyChanges]</c> would otherwise
    ///     weave, the same way <see cref="IgnoreAutosaveAttribute"/> opts a property out of a
    ///     class-level <c>[Autosave]</c>. The framework's own <c>FileName</c>, <c>Modulation</c> and
    ///     <c>IVersionable.Version</c> are excluded automatically and need no attribute.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class IgnoreNotifyAttribute : Attribute { }
}
