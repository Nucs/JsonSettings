using System;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     Suspends auto-saving until SuspendAutosave.Dispose or SuspendAutosave.Resume are called.<br/>
    ///     If changes are introduced while suspension then a save will be commited and resume or disposal.
    /// </summary>
    public readonly struct SuspendAutosave : IDisposable {
        private readonly AutosaveModule _module;

        public SuspendAutosave(AutosaveModule module) {
            _module = module;
            module.AutosavingState = AutosavingState.Suspended;
        }

        public void Resume() {
            Dispose();
        }

        public void Dispose() {
            if (_module.AutosavingState == AutosavingState.SuspendedChanged)
                _module.TryTriggerSave();
            
            _module.AutosavingState = AutosavingState.Running;
        }
    }
}