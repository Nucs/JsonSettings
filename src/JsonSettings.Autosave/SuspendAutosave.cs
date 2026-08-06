// This project deliberately does NOT set <Nullable>enable</Nullable> -- doing so project-wide
// is deferred work with its own warning wave (see the note in Directory.Build.targets). This
// file moved here from the base JsonSettings project, which DOES enable nullable, so its
// _settings field carries a `?` annotation. Turn on just the annotation context for this one
// file so the annotation stays legal without opting the project into flow analysis. Without it
// the compiler raises CS8632, which TreatWarningsAsErrors promotes to a build-breaking error
// (CS8632 is absent from the WarningsNotAsErrors allowlist in Directory.Build.targets).
#nullable enable annotations

using System;
using Nucs.JsonSettings.Modulation;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     Suspends auto-saving until SuspendAutosave.Dispose or SuspendAutosave.Resume are called.<br/>
    ///     If changes are introduced while suspension then a save will be commited and resume or disposal.
    /// </summary>
    /// <remarks>
    ///     Wraps the shared <see cref="SuspensionModule"/> base rather than a concrete module, so
    ///     the same struct suspends the woven path's <see cref="AutosaveModule"/> and
    ///     <see cref="SettingsBag"/>'s <see cref="SettingsBagAutosaveModule"/> alike.
    /// </remarks>
    public readonly struct SuspendAutosave : IDisposable {
        private readonly SuspensionModule _module;

        /// <summary>
        ///     A strong reference to the settings instance, held for the entire lifetime of the
        ///     suspension.
        /// </summary>
        /// <remarks>
        ///     A suspension can owe a save: any change made while suspended sets
        ///     <see cref="AutosavingState.SuspendedChanged"/> and defers the write to
        ///     <see cref="Dispose"/>. The only path back to the settings instance used to be
        ///     <see cref="SuspensionModule.TryTriggerSave"/>, which resolves the module's
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

        public SuspendAutosave(SuspensionModule module) {
            _module = module;
            _settings = module.TryGetSettings();
            module.EnterSuspension();
        }

        public void Resume() {
            Dispose();
        }

        public void Dispose() {
            //ExitSuspension returns true only when the OUTERMOST scope closes with a change owed,
            //and it flips the state back to Running itself. Nesting is reference-counted there, so
            //an inner using-block no longer ends the outer scope's suspension early. Calling this a
            //second time (Resume then Dispose) is a no-op: depth is already zero and the state is
            //Running, so nothing is owed.
            if (_module.ExitSuspension()) {
                if (_settings != null)
                    _settings.Save();
                else
                    //the module was already detached when the suspension was opened; fall back to
                    //the socket in case it has been re-attached since.
                    _module.TryTriggerSave();
            }
        }
    }
}