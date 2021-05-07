using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Castle.DynamicProxy;
using Newtonsoft.Json;
using BindingFlags = System.Reflection.BindingFlags;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     Intercepts and performs autosave on write
    /// </summary>
    [Serializable]
    public class JsonSettingsAutosaveInterceptor : IInterceptor {
        private readonly JsonSettings _settings;
        private readonly HashSet<string> _monitoredProperties;
        private AutosaveModule _module;

        public JsonSettingsAutosaveInterceptor(JsonSettings settings) {
            _settings = settings;
            _settings.Modulation.Attach(_module = new AutosaveModule());
            //populate information
            _monitoredProperties = new HashSet<string>(_settings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                                                .Where(p => p.GetSetMethod()?.IsVirtual == true
                                                                            && p.GetCustomAttribute<JsonIgnoreAttribute>(true) == null && p.GetCustomAttribute<IgnoreAutosaveAttribute>(true) == null)
                                                                .Select(prop => prop.Name));
        }

        [MethodImpl(512)]
        public void Intercept(IInvocation invocation) {
            invocation.Proceed();

            //handle saving if it was a setter
            if (_module.AutosavingState != AutosavingState.SuspendedChanged && invocation.Method.Name.StartsWith("set_", StringComparison.Ordinal)) {
                var propName = invocation.Method.Name.Substring(4);
                if (!_monitoredProperties.Contains(propName))
                    return;

                for (var i = 0; i < AutosaveModule._frameworkParametersLength; i++) {
                    if (AutosaveModule._frameworkParameters[i] == propName) return;
                }

                //save.
                if (_module.UpdatesSuspended) {
                    _module.AutosavingState = AutosavingState.SuspendedChanged;
                } else
                    _settings.Save();
            }
        }
    }
}