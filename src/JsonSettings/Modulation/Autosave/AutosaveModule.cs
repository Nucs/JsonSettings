using System;
using System.Collections.Generic;
using Module = Nucs.JsonSettings.Modulation.Module;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     The shared save-suspension state a settings instance carries while autosave is enabled.
    /// </summary>
    /// <remarks>
    ///     This stays in the base package on purpose: <see cref="SettingsBag"/>'s dictionary-backed
    ///     autosave and <c>JsonSettings.LoadJson</c>'s load-suppression both drive this state with the
    ///     base package alone, and the woven path in <c>Nucs.JsonSettings.Autosave</c> reuses the very
    ///     same instance. All of the weaving-specific reflection -- the opt-in rules, the
    ///     <c>NotificationBinder</c>, the <c>SuspendAutosave</c> entry point -- lives in that package
    ///     instead; this type keeps only the flags and the reference-counted suspension machine.
    /// </remarks>
    public class AutosaveModule : Module {
        /// <summary>
        ///     True while this module is committing an autosave, so that a write made from inside a
        ///     save (typically an <c>AfterSave</c> handler that touches a monitored property) does
        ///     not trigger another autosave and recurse until the stack overflows.
        /// </summary>
        /// <remarks>
        ///     Without this, setting a monitored property from an <c>AfterSave</c> handler was
        ///     unbounded recursion -- an uncatchable process crash rather than a bug you could
        ///     debug. The value written from inside the save is still kept in memory and persists on
        ///     the next save; it simply does not re-enter the writer that is already running.
        /// </remarks>
        internal bool IsSaving { get; set; }

        /// <summary>
        ///     True while the settings instance is being populated by a load. Deserialization sets
        ///     every property through its (woven) setter, so without this a load performed after
        ///     autosave was enabled -- <c>Load()</c>, <c>LoadDefault()</c>, a versioning reload --
        ///     would commit one autosave per property and write the half-loaded object back to
        ///     disk mid-load. The load path raises this around the populate step.
        /// </summary>
        internal bool IsLoading { get; set; }

        //Depth of nested SuspendAutosave scopes. Suspension must be reference-counted: an inner
        //scope disposing used to reset the state straight back to Running, so the OUTER scope
        //stopped suspending halfway through and committed a save it was supposed to be batching.
        private int _suspensionDepth;

        /// <summary>
        ///     Opens one level of suspension. Only the outermost transition (Running -> Suspended)
        ///     changes the state; a nested Enter must not clobber a pending <see cref="AutosavingState.SuspendedChanged"/>.
        /// </summary>
        internal void EnterSuspension() {
            _suspensionDepth++;
            if (AutosavingState == AutosavingState.Running)
                AutosavingState = AutosavingState.Suspended;
        }

        /// <summary>
        ///     Closes one level of suspension. Returns true only when the outermost scope closes
        ///     with a change owed, i.e. the caller should now commit the single batched save.
        /// </summary>
        internal bool ExitSuspension() {
            if (_suspensionDepth > 0)
                _suspensionDepth--;
            if (_suspensionDepth > 0)
                return false; //still nested; keep suspending

            var owed = AutosavingState == AutosavingState.SuspendedChanged;
            AutosavingState = AutosavingState.Running;
            return owed;
        }

        /// <summary>
        ///     The property names a write to which commits a save, resolved once when autosave is
        ///     enabled.
        /// </summary>
        /// <remarks>
        ///     This used to be computed in the interceptor's constructor, which only existed
        ///     because a proxy existed. Weaving has no interceptor to hang it on, and the woven
        ///     advice must not pay for reflection on every single property write, so the set is
        ///     resolved once here and consulted as a hash lookup thereafter.
        ///
        ///     Null means "autosave was attached without a property filter" -- nothing is
        ///     monitored -- rather than "everything is monitored", so a module that somehow
        ///     reaches the advice half-initialized stays silent instead of saving on every write.
        /// </remarks>
        private HashSet<string>? _monitoredProperties;

        /// <summary>
        ///     Records which properties this module saves on. Called by EnableAutosave.
        /// </summary>
        internal void SetMonitoredProperties(HashSet<string> monitored) {
            _monitoredProperties = monitored;
        }

        /// <summary>
        ///     Whether a write to <paramref name="propertyName"/> should commit a save.
        /// </summary>
        public bool IsMonitored(string propertyName) {
            return _monitoredProperties != null && _monitoredProperties.Contains(propertyName);
        }

        /// <summary>
        ///     When true, changes will not cause updates.
        /// </summary>
        public virtual bool UpdatesSuspended => AutosavingState != AutosavingState.Running;

        /// <summary>
        ///     The state of the autosave module
        /// </summary>
        public virtual AutosavingState AutosavingState { get; set; }

        /// <summary>
        ///     The notification handler taking care of binding and unbinding to property and collection changes.
        /// </summary>
        /// <remarks>
        ///     Typed as <see cref="IDisposable"/> so the base package holds only the lifetime, not the
        ///     autosave-specific binder itself: <c>Nucs.JsonSettings.Autosave</c> assigns a
        ///     <c>NotificationBinder</c> here and this module disposes it when it is torn down.
        ///     The load pipeline additionally pattern-matches this for <see cref="INotificationsHandler"/>
        ///     to resync the binder after a populate replaced collection instances; the property stays
        ///     <see cref="IDisposable"/> so existing assignments keep compiling and binding.
        /// </remarks>
        public IDisposable? NotificationsHandler { get; set; }

        /// <summary>
        ///     Will try to trigger save if this module did not lose reference to <see cref="JsonSettings"/> socket.
        /// </summary>
        public void TryTriggerSave() {
            if (Socket != null && Socket.TryGetTarget(out var settings))
                settings.Save();
        }

        /// <summary>
        ///     Resolves a strong reference to the <see cref="JsonSettings"/> this module is attached to,
        ///     or null when the module is detached or the instance has already been collected.
        /// </summary>
        /// <remarks>
        ///     <see cref="Module.Socket"/> is a <see cref="WeakReference{T}"/>, so anything that must
        ///     still be able to reach the settings later has to hold on to the returned reference for
        ///     that whole period rather than re-resolving the socket on demand.
        ///     See <c>SuspendAutosave</c>.
        /// </remarks>
        internal JsonSettings? TryGetSettings() {
            if (Socket != null && Socket.TryGetTarget(out var settings))
                return settings;
            return null;
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);
            NotificationsHandler?.Dispose();
        }
    }

    public enum AutosavingState : byte {
        Running,
        Suspended,
        /// <summary>
        ///     There happened a change during <see cref="Suspended"/>
        /// </summary>
        SuspendedChanged
    }
}
