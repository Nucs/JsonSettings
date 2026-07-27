using System;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     Suspends auto-saving until SuspendAutosave.Dispose or SuspendAutosave.Resume are called.<br/>
    ///     If changes are introduced while suspension then a save will be commited and resume or disposal.
    /// </summary>
    public readonly struct SuspendAutosave : IDisposable {
        private readonly AutosaveModule _module;

        /// <summary>
        ///     A strong reference to the settings instance, held for the entire lifetime of the
        ///     suspension.
        /// </summary>
        /// <remarks>
        ///     A suspension can owe a save: any change made while suspended sets
        ///     <see cref="AutosavingState.SuspendedChanged"/> and defers the write to
        ///     <see cref="Dispose"/>. The only path back to the settings instance used to be
        ///     <see cref="AutosaveModule.TryTriggerSave"/>, which resolves the module's
        ///     <see cref="System.WeakReference{T}"/> socket -- and a module does not keep its
        ///     settings alive. So if nothing else referenced the settings for the duration of the
        ///     scope (which the JIT is free to arrange as soon as the caller's last use of it is
        ///     inside the scope) a garbage collection could reclaim the instance mid-suspension,
        ///     TryTriggerSave would find a dead socket, and the owed write was dropped without an
        ///     exception, a log line or any other trace -- silent data loss.
        ///
        ///     Holding the reference here is the fix and also the correct model: while a
        ///     suspension is open the settings object is not garbage, because a write against it
        ///     is still pending.
        /// </remarks>
        private readonly JsonSettings? _settings;

        public SuspendAutosave(AutosaveModule module) {
            _module = module;
            _settings = module.TryGetSettings();
            module.AutosavingState = AutosavingState.Suspended;
        }

        public void Resume() {
            Dispose();
        }

        public void Dispose() {
            if (_module.AutosavingState == AutosavingState.SuspendedChanged) {
                if (_settings != null)
                    _settings.Save();
                else
                    //the module was already detached when the suspension was opened; fall back to
                    //the socket in case it has been re-attached since.
                    _module.TryTriggerSave();
            }

            _module.AutosavingState = AutosavingState.Running;
        }
    }
}