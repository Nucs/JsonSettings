using System;

namespace Nucs.JsonSettings.NotifyChanges {
    /// <summary>
    ///     Decides, per woven setter, whether an assignment is allowed to raise a change
    ///     notification. Consumed by <see cref="NotifyChangesAttribute"/> and
    ///     <see cref="NotifyChangesMixinAttribute"/>.
    /// </summary>
    /// <remarks>
    ///     A <see cref="FlagsAttribute"/> enum so guards compose: the common
    ///     "notify only on a real, meaningful change" is <c>OnlyChanged | SkipNullOrDefault</c>.
    ///     <see cref="Always"/> is the absence of every guard (value <c>0</c>), so ORing it with
    ///     anything is a no-op, which is the intended reading -- it means "no filtering".
    /// </remarks>
    [Flags]
    public enum NotificationGuard {
        /// <summary>
        ///     Raise on every setter invocation, whatever the value. This is "any setter access":
        ///     the notification is produced for each write, with no equality or null filtering.
        /// </summary>
        Always = 0,

        /// <summary>
        ///     Raise only when the incoming value differs from the property's current value
        ///     (compared with <see cref="object.Equals(object, object)"/>). This is the default and
        ///     mirrors the hand-written <c>if (value == _field) return;</c> guard that idiomatic
        ///     <see cref="System.ComponentModel.INotifyPropertyChanged"/> setters use.
        /// </summary>
        /// <remarks>
        ///     Evaluating this reads the property's getter <em>before</em> the setter runs, so the
        ///     old value can be compared to the new one. A write-only property therefore behaves as
        ///     <see cref="Always"/> for this bit (there is no getter to read).
        /// </remarks>
        OnlyChanged = 1,

        /// <summary>
        ///     Suppress the notification when the incoming value is <c>null</c> or the type's default
        ///     (<c>0</c>, <c>false</c>, <c>default(T)</c>). Useful when clearing a property back to
        ///     its default should not disturb bindings. Combine with <see cref="OnlyChanged"/> to get
        ///     "notify only on a change to a non-default value".
        /// </summary>
        SkipNullOrDefault = 2,
    }
}
