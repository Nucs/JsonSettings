using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Nucs.JsonSettings.Modulation;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     The opt-in rules that decide which properties autosave watches. Lifted out of
    ///     <see cref="AutosaveModule"/> -- which stays in the base package as the shared
    ///     save-suspension state -- so every piece of weaving-specific reflection lives in this
    ///     package instead of shipping inert in <c>Nucs.JsonSettings</c>.
    /// </summary>
    internal static class AutosavePolicy {
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
                   && !IsVersionableVersion(property)
                   && property.GetCustomAttribute<JsonIgnoreAttribute>(true) is null
                   && property.GetCustomAttribute<IgnoreAutosaveAttribute>(true) is null
                   && _frameworkParameters.All(f => f != property.Name);
        }

        /// <summary>
        ///     The <see cref="IVersionable.Version"/> property on a versionable settings class.
        /// </summary>
        /// <remarks>
        ///     Version is framework metadata managed by <c>VersioningModule</c>, not a user setting:
        ///     the module writes it during load, recovery and default-loading (e.g.
        ///     <c>tsender.Version = ExpectedVersion</c>). Monitoring it means a reload that
        ///     normalises the version, or any framework version write while autosave is live,
        ///     commits an autosave the user never asked for. It rides along in every ordinary save
        ///     already, so excluding it from the *trigger* set loses nothing -- exactly the reason
        ///     FileName and Modulation are excluded. The check is scoped to
        ///     <see cref="IVersionable"/> so a user's own unrelated property named "Version" is
        ///     unaffected.
        /// </remarks>
        private static bool IsVersionableVersion(PropertyInfo property) {
            return property.Name == nameof(IVersionable.Version)
                   && property.DeclaringType != null
                   && typeof(IVersionable).IsAssignableFrom(property.DeclaringType);
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
    }
}
