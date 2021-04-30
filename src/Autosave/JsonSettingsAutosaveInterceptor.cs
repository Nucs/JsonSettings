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

        public JsonSettingsAutosaveInterceptor(JsonSettings settings) {
            _settings = settings;
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
            if (invocation.Method.Name.StartsWith("set_", StringComparison.Ordinal)) {
                var propName = invocation.Method.Name.Substring(4);
                if (!_monitoredProperties.Contains(propName))
                    return;
                
                for (var i = 0; i < JsonSettingsAutosaveExtensions._frameworkParametersLength; i++) {
                    if (JsonSettingsAutosaveExtensions._frameworkParameters[i] == propName) return;
                }

                //save.
                _settings.Save();
            }
        }
    }
}