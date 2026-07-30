using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Nucs.JsonSettings.Modulation;
using Module = Nucs.JsonSettings.Modulation.Module;

namespace Nucs.JsonSettings.Autosave {
    public class AutosaveModule : Module {
        internal static readonly string[] _frameworkParameters = {nameof(JsonSettings.FileName), nameof(JsonSettings.Modulation)};
        internal static readonly int _frameworkParametersLength = _frameworkParameters.Length;

        /// <summary>
        ///     Whether a property is opted into autosave at all -- i.e. not excluded by
        ///     <see cref="JsonIgnoreAttribute"/> or <see cref="IgnoreAutosaveAttribute"/> and not one
        ///     of the framework's own properties (FileName, Modulation).
        /// </summary>
        /// <remarks>
        ///     This is the one place the opt-out rule is written down. It used to be duplicated
        ///     across the woven-path resolver in <c>AutosaveRuntime</c>, the old interceptor
        ///     constructors, and <see cref="NotificationBinder"/>, and they had drifted -- most
        ///     visibly, <see cref="NotificationBinder"/> also required <c>virtual</c> where the save
        ///     path no longer did, and it ignored <see cref="IgnoreAutosaveAttribute"/> when binding
        ///     collection fields, so an <c>[IgnoreAutosave]</c> collection still saved on mutation.
        /// </remarks>
        internal static bool IsAutosaveOptedIn(PropertyInfo property) {
            return property.GetIndexParameters().Length == 0
                   && property.GetCustomAttribute<JsonIgnoreAttribute>(true) == null
                   && property.GetCustomAttribute<IgnoreAutosaveAttribute>(true) == null
                   && _frameworkParameters.All(f => f != property.Name);
        }

        /// <summary>
        ///     Whether a write to this property should commit a save. Requires a setter (public or
        ///     not, so <c>{ get; private set; }</c> counts): only an assignable property has a woven
        ///     setter for the advice to run in.
        /// </summary>
        internal static bool IsAutosaveMonitored(PropertyInfo property) {
            return property.GetSetMethod(true) != null && IsAutosaveOptedIn(property);
        }

        /// <summary>
        ///     Whether the current value of this property should be watched for nested
        ///     <see cref="System.ComponentModel.INotifyPropertyChanged"/> /
        ///     <see cref="System.Collections.Specialized.INotifyCollectionChanged"/> changes.
        /// </summary>
        /// <remarks>
        ///     Deliberately does NOT require a setter, unlike <see cref="IsAutosaveMonitored"/>: a
        ///     get-only <c>ObservableCollection</c> is the idiomatic way to expose a mutable list you
        ///     never reassign, and its contents changing still has to save. Only a readable,
        ///     opted-in property qualifies.
        /// </remarks>
        internal static bool IsNotificationBindable(PropertyInfo property) {
            return property.GetGetMethod(true) != null && IsAutosaveOptedIn(property);
        }

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