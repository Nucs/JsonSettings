using Nucs.JsonSettings.Modulation;

namespace Nucs.JsonSettings.Autosave {
    public class AutosaveModule : Module {
        internal static readonly string[] _frameworkParameters = {nameof(JsonSettings.FileName), nameof(JsonSettings.Modulation)};
        internal static readonly int _frameworkParametersLength = _frameworkParameters.Length;
        
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
        public NotificationBinder? NotificationsHandler { get; set; }

        /// <summary>
        ///     Suspends auto-saving until SuspendAutosave.Dispose or SuspendAutosave.Resume are called.<br/>
        ///     If changes are introduced while suspension then a save will be commited and resume or disposal.
        /// </summary>
        public SuspendAutosave SuspendAutosave() {
            return new SuspendAutosave(this);
        }

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
        ///     See <see cref="Autosave.SuspendAutosave"/>.
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