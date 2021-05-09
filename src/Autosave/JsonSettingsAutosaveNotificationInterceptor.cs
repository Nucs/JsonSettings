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
        private AutosaveModule _module;

        public JsonSettingsAutosaveNotificationInterceptor(JsonSettings settings) {
            _settings = settings;
        }

        public JsonSettingsAutosaveNotificationInterceptor(JsonSettings settings, NotificationBinder notificationsHandler) {
            _settings = settings;
            _settings.Modulation.Attach(_module = new AutosaveModule());
            _module.NotificationsHandler = notificationsHandler;
        }

        [MethodImpl(512)]
        public void Intercept(IInvocation invocation) {
            invocation.Proceed();

            //handle saving if it was a setter, not INotifyPropertyChanged/INotifyCollectionChanged and has CompilerGeneratedAttribute
            if (_module.AutosavingState != AutosavingState.SuspendedChanged
                && invocation.Method.ReturnType == typeof(void)
                && invocation.Arguments.Length > 0
                && invocation.Method.Name.StartsWith("set_", StringComparison.Ordinal)) {
                var type = invocation.Arguments[0]?.GetType();
                if (type != null
                    && typeof(INotifyPropertyChanged).IsAssignableFrom(type) != true
                    && typeof(INotifyCollectionChanged).IsAssignableFrom(type) != true
                    && invocation.MethodInvocationTarget.IsDefined(typeof(CompilerGeneratedAttribute), false)) {
                    var propName = invocation.Method.Name.Substring(4);
                    if (!_module.NotificationsHandler!.CanHandleProperty(propName))
                        return;

                    //save.
                    if (_module.UpdatesSuspended) {
                        _module.AutosavingState = AutosavingState.SuspendedChanged;
                    } else
                        _settings.Save();
                }
            }
        }

        #region IDisposable

        public void Dispose() {
            _module.Dispose();
        }

        #endregion
    }
}