using System;
using Nucs.JsonSettings.Autosave;

namespace Nucs.JsonSettings.Modulation {
    /// <summary>
    ///     The neutral save-suspension state a settings instance carries while some flavour of
    ///     autosave is enabled: the re-entrancy and loading gates, and the reference-counted
    ///     suspension machine.
    /// </summary>
    /// <remarks>
    ///     This stays in the base package on purpose: <see cref="SettingsBag"/>'s dictionary-backed
    ///     autosave drives these gates with the base package alone, through its own
    ///     <see cref="SettingsBagAutosaveModule"/>, and nothing here knows about weaving. The woven
    ///     path's module -- the monitored-property set and the notification-binder slot -- derives
    ///     from this as <c>AutosaveModule</c> in <c>Nucs.JsonSettings.Autosave</c>, and that
    ///     package's <c>SuspendAutosave</c> drives either kind through this shared type, which is
    ///     what keeps the two autosave paths from ever drifting apart on re-entrancy or suspension
    ///     semantics.
    ///
    ///     The loading gate is self-managed: <see cref="Attach"/> subscribes to the socket's
    ///     repopulate events and brackets <see cref="IsLoading"/> around every populate, so the
    ///     load pipeline needs no knowledge of any module type to keep a load from autosaving the
    ///     half-loaded object back to disk.
    /// </remarks>
    public class SuspensionModule : Module {
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
        ///     disk mid-load.
        /// </summary>
        /// <remarks>
        ///     Bracketed by this module itself, not by the load pipeline: <see cref="Attach"/>
        ///     subscribes to the socket's repopulate events and raises/drops the flag around every
        ///     populate. <c>JsonSettings.LoadJson</c> used to reach into the first attached module
        ///     and set this directly, which coupled the load pipeline to the module type and
        ///     covered exactly one module; self-subscription gates every attached module and lets
        ///     the pipeline know nothing about autosave.
        /// </remarks>
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
        ///     When true, changes will not cause updates.
        /// </summary>
        public virtual bool UpdatesSuspended => AutosavingState != AutosavingState.Running;

        /// <summary>
        ///     The state of the autosave module
        /// </summary>
        public virtual AutosavingState AutosavingState { get; set; }

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

        /// <summary>
        ///     Attaches and self-wires the loading gate: subscribes to the socket's repopulate
        ///     events so <see cref="IsLoading"/> brackets every populate of this instance.
        /// </summary>
        public override void Attach(JsonSettings socket) {
            base.Attach(socket);
            socket.BeforeRepopulate += OnSocketBeforeRepopulate;
            socket.AfterRepopulate += OnSocketAfterRepopulate;
        }

        public override void Deattach(JsonSettings socket) {
            base.Deattach(socket);
            socket.BeforeRepopulate -= OnSocketBeforeRepopulate;
            socket.AfterRepopulate -= OnSocketAfterRepopulate;
        }

        private void OnSocketBeforeRepopulate(JsonSettings sender) {
            IsLoading = true;
        }

        //Runs from LoadJson's finally, so a populate that threw halfway still drops the gate --
        //autosave must resume after a failed load exactly as after a successful one.
        private void OnSocketAfterRepopulate(JsonSettings sender) {
            IsLoading = false;
        }
    }
}
