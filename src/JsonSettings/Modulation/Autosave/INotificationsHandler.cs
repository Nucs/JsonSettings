using System;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     The contract between the base package and the notification binder the
    ///     <c>Nucs.JsonSettings.Autosave</c> package parks on <see cref="AutosaveModule.NotificationsHandler"/>.
    /// </summary>
    /// <remarks>
    ///     The base package cannot name the binder type -- it lives in the autosave package -- but
    ///     the load pipeline must be able to tell it "the object graph you bound against has been
    ///     repopulated": collection properties deserialize with Replace semantics (see the contract
    ///     resolver in <see cref="JsonSettings"/>), so after any <c>Load</c>/<c>LoadDefault</c> the
    ///     instances the binder subscribed to may no longer be the ones the settings object holds,
    ///     and mutating the replacements would never save. <see cref="Resync"/> is that signal;
    ///     disposal stays the module's job exactly as before.
    /// </remarks>
    public interface INotificationsHandler : IDisposable {
        /// <summary>
        ///     Re-reads every monitored property and moves the change subscriptions from the value
        ///     bound previously to the value the property holds now. Must not save: it runs inside
        ///     the load pipeline, where a save would commit a half-loaded object.
        /// </summary>
        void Resync();
    }
}
