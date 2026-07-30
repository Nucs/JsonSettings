using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Nucs.JsonSettings.Modulation;
using BindingFlags = System.Reflection.BindingFlags;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     The runtime half of <see cref="AutosaveAttribute"/>. Infrastructure -- called from woven
    ///     setters, not intended to be called directly.
    /// </summary>
    public static class AutosaveRuntime {
        /// <summary>
        ///     Invoked at the end of every woven instance setter.
        /// </summary>
        /// <remarks>
        ///     This is the whole hot path of autosave, so it is written to bail out as early as
        ///     possible. In particular it must be inert -- and cheap -- on instances that never had
        ///     <c>EnableAutosave()</c> called on them, because <see cref="AutosaveAttribute"/> is
        ///     woven into the type, not into the instance: every instance of a marked class runs
        ///     this on every write, including the ones Newtonsoft assigns while deserializing
        ///     during <c>Load</c>, which happens before any module is attached.
        ///
        ///     This mirrors what <c>JsonSettingsAutosaveInterceptor.Intercept</c> did after
        ///     <c>invocation.Proceed()</c>, minus the "is this a set_ method" string test, which
        ///     the weaver has already answered by construction.
        /// </remarks>
        /// <param name="instance">The object whose setter just ran.</param>
        /// <param name="propertyName">The property name, baked in at weave time.</param>
        public static void OnPropertySet(object instance, string propertyName) {
            if (!(instance is JsonSettings settings))
                return;

            var module = TryGetAutosaveModule(settings);
            if (module == null)
                return; //autosave was never enabled on this instance

            if (module.IsSaving)
                return; //re-entered from inside this module's own Save (e.g. an AfterSave handler
                        //that writes a monitored property); saving again would recurse forever

            if (module.IsLoading)
                return; //a load is populating this instance through the woven setters; those writes
                        //come from disk, not from the user, and must not save back

            if (module.AutosavingState == AutosavingState.SuspendedChanged)
                return; //a save is already owed; nothing further to record

            if (!module.IsMonitored(propertyName))
                return;

            if (module.UpdatesSuspended) {
                module.AutosavingState = AutosavingState.SuspendedChanged;
            } else {
                module.IsSaving = true;
                try {
                    settings.Save();
                } finally {
                    module.IsSaving = false;
                }
            }
        }

        /// <summary>
        ///     Resolves the attached <see cref="AutosaveModule"/>, or null when there is none.
        /// </summary>
        /// <remarks>
        ///     <see cref="ModuleSocket.GetModule{T}"/> throws <see cref="ModularityException"/>
        ///     when the module is absent, and absent is the overwhelmingly common case here -- it
        ///     is every write to every un-enabled instance. Raising and catching an exception on
        ///     that path would be pathologically slow, so the list is scanned directly.
        /// </remarks>
        private static AutosaveModule TryGetAutosaveModule(JsonSettings settings) {
            var modules = settings.Modulation.Modules;
            var len = modules.Count;
            for (int i = 0; i < len; i++) {
                if (modules[i] is AutosaveModule module)
                    return module;
            }

            return null;
        }

        /// <summary>
        ///     The set of property names a write to which commits a save, for a settings type.
        /// </summary>
        /// <remarks>
        ///     The predicate is <see cref="AutosaveModule.IsAutosaveMonitored"/>, shared with
        ///     <see cref="NotificationBinder"/> so the two cannot disagree. It relaxes the old
        ///     interceptor's <c>GetSetMethod()?.IsVirtual == true</c> test: that requirement existed
        ///     solely because a Castle class proxy can only override virtual members, and weaving
        ///     rewrites the setter itself, so non-virtual properties are now first-class. Non-public
        ///     setters are included so that <c>public string Foo { get; private set; }</c> is
        ///     monitored; the property itself is still public, matching what gets serialized.
        /// </remarks>
        internal static HashSet<string> ResolveMonitoredProperties(Type settingsType) {
            var monitored = settingsType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                        .Where(AutosaveModule.IsAutosaveMonitored)
                                        .Select(p => p.Name);

            return new HashSet<string>(monitored, StringComparer.Ordinal);
        }
    }
}
