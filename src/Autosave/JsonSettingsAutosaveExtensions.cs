using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using Castle.DynamicProxy;
using Nucs.JsonSettings.Examples;

namespace Nucs.JsonSettings.Autosave {
    public static class JsonSettingsAutosaveExtensions {
        internal static readonly string[] _frameworkParameters = {"FileName"};
        internal static readonly int _frameworkParametersLength = _frameworkParameters.Length;

        private static ProxyGenerator _generator;

        /// <summary>
        ///     Enables automatic saving when changing any <b>virtual properties</b>.
        /// </summary>
        /// <typeparam name="TSettings">A settings class implementing <see cref="JsonSettings"/></typeparam>
        /// <param name="settings">The settings class to wrap in a proxy.</param>
        /// <returns></returns>
        /// <exception cref="JsonSettingsException">When <typeparamref name="TSettings"/> has no virtual properties.</exception>
        public static TSettings EnableAutosave<TSettings>(this TSettings settings) where TSettings : JsonSettings {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _generator ??= new ProxyGenerator();
            return _generator.CreateClassProxyWithTarget<TSettings>(settings, ApplicableInterceptors(settings).ToArray());
        }

        /// <summary>
        ///     Enables automatic saving when changing any <b>virtual properties</b> returning the interface of <typeparamref name="ISettings"/>.
        /// </summary>
        /// <typeparam name="ISettings">An interface, your <see cref="JsonSettings"/> is implementing</typeparam>
        /// <param name="settings">The settings class to wrap in a proxy.</param>
        /// <returns>The interface specified which will save on every set</returns>
        /// <exception cref="JsonSettingsException">When <typeparamref name="TSettings"/> has no virtual properties.</exception>
        public static ISettings EnableIAutosave<ISettings>(this JsonSettings settings) where ISettings : class {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            _generator ??= new ProxyGenerator();

            if (!(settings is ISettings))
                throw new InvalidCastException($"Settings class '{settings.GetType().FullName}' does not implement interface '{typeof(ISettings).FullName}'");

            return _generator.CreateInterfaceProxyWithTarget<ISettings>((ISettings) (object) settings, ApplicableInterceptors(settings).ToArray());
        }

        public static IEnumerable<IInterceptor> ApplicableInterceptors<TSettings>(this TSettings settings) where TSettings : JsonSettings {
            var settingsType = settings.GetType();

            //if it doesn't contain any virtual methods, throw for the developer to know about it.
            if (!settingsType.GetProperties()
                             .Where(p => _frameworkParameters.All(av => !p.Name.Equals(av)))
                             .Any(p => p.GetGetMethod().IsVirtual)) {
                var msg = $"JsonSettings: During proxy creation of {settingsType.Name}, no virtual properties were found which will make Autosaving redundant.";
                try {
                    Debug.WriteLine(msg);
                    if (Debugger.IsAttached)
                        Console.Error.WriteLine(msg);
                } catch (Exception) {
                    //swallow
                }

                throw new JsonSettingsException(msg);
            }

            IInterceptor interceptor;
            if (settings is NotifiyingJsonSettings notifiying)
                interceptor = new JsonSettingsAutosaveNotificationInterceptor(settings, new NotificationBinder(notifiying));
            else
                interceptor = new JsonSettingsAutosaveInterceptor(settings);

            yield return interceptor;
        }
    }
}