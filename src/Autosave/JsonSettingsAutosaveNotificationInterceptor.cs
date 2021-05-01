using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Castle.DynamicProxy;
using Newtonsoft.Json;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     Intercepts and binds to NotificationChanged property.
    /// </summary>
    [Serializable]
    public class JsonSettingsAutosaveNotificationInterceptor : IInterceptor, IDisposable {
        private readonly JsonSettings _settings;
        private readonly NotificationBinder _notificationsHandler;

        public JsonSettingsAutosaveNotificationInterceptor(JsonSettings settings) {
            _settings = settings;
        }

        public JsonSettingsAutosaveNotificationInterceptor(JsonSettings settings, NotificationBinder notificationsHandler) {
            _settings = settings;
            _notificationsHandler = notificationsHandler;
        }

        [MethodImpl(512)]
        public void Intercept(IInvocation invocation) {
            invocation.Proceed();

            //handle saving if it was a setter, not INotifyPropertyChanged/INotifyCollectionChanged and has CompilerGeneratedAttribute
            if (invocation.Method.ReturnType == typeof(void) && invocation.Arguments.Length > 0 && invocation.Method.Name.StartsWith("set_", StringComparison.Ordinal)) {
                var valueType = invocation.Arguments[0].GetType();
                if (typeof(INotifyPropertyChanged).IsAssignableFrom(valueType) != true
                    && typeof(INotifyCollectionChanged).IsAssignableFrom(valueType) != true
                    && invocation.MethodInvocationTarget.IsDefined(typeof(CompilerGeneratedAttribute), false)) {
                    var propName = invocation.Method.Name.Substring(4);
                    if (!_notificationsHandler.CanHandleProperty(propName))
                        return;
                    
                    for (var i = 0; i < JsonSettingsAutosaveExtensions._frameworkParametersLength; i++) {
                        if (JsonSettingsAutosaveExtensions._frameworkParameters[i] == propName) return;
                    }

                    //save.
                    _settings.Save();
                }
            }
        }

        #region IDisposable

        public void Dispose() {
            _notificationsHandler.Dispose();
        }

        #endregion
    }
}