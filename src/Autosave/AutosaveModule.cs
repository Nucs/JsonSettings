using Nucs.JsonSettings.Modulation;

namespace Nucs.JsonSettings.Autosave {
    public class AutosaveModule : Module {
        /// <summary>
        ///     When true, changes will not cause updates.
        /// </summary>
        public bool UpdatesSuspended => AutosavingState != AutosavingState.Running;

        /// <summary>
        ///     The state of the autosave module
        /// </summary>
        public AutosavingState AutosavingState { get; set; }

        /// <summary>
        ///     The notification handler taking care of binding and unbinding to property and collection changes.
        /// </summary>
        public NotificationBinder NotificationsHandler { get; set; }

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
            if (Socket.TryGetTarget(out var settings))
                settings.Save();
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);
            NotificationsHandler.Dispose();
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