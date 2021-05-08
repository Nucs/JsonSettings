using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Castle.DynamicProxy;
using Nucs.JsonSettings.Examples;
using BindingFlags = System.Reflection.BindingFlags;

namespace Nucs.JsonSettings.Autosave {
    internal static class TypeValidation<T> where T : JsonSettings {
        // ReSharper disable once StaticMemberInGenericType
        private static bool _validated;

        public static void ValidateAllVirtual() {
            if (_validated || !typeof(T).IsInterface && typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                                                 .Where(p => AutosaveModule._frameworkParameters.All(av => !p.Name.Equals(av)))
                                                                 .All(p => p.GetGetMethod().IsVirtual)) {
                _validated = true;
                return;
            }

            var msg = $"JsonSettings: During proxy creation of {typeof(T).Name}, any non-virtual properties will be completely ignored by the proxy object and any change to" +
                      $" non-virtual properties will do nothing therefore all properties must be virtual in order for the proxied object to function normally " +
                      $"or proxify with an interface which forces the property to be virtual behind the scenes (only the interface properties will be validated).";
            try {
                Debug.WriteLine(msg);
                if (Debugger.IsAttached)
                    Console.Error.WriteLine(msg);
            } catch (Exception) {
                //swallow
            }

            throw new JsonSettingsException(msg);
        }

        internal static class InterfaceTypeValidation<TInterface> where TInterface : class {
            // ReSharper disable once StaticMemberInGenericType
            // ReSharper disable once MemberHidesStaticFromOuterClass
            private static bool _validated;

            public static void ValidateInterfaceVirtual() {
                if (_validated || typeof(TInterface).IsInterface
                    && typeof(TInterface).IsAssignableFrom(typeof(T))) {
                    _validated = true;
                    return;
                }

                var msg = $"JsonSettings: During proxy creation of {typeof(T).Name}, any non-virtual properties will be completely ignored by the proxy object and any change to" +
                          $" non-virtual properties will do nothing therefore all properties must be virtual in order for the proxied object to function normally " +
                          $"or proxify with an interface which forces the property to be virtual behind the scenes (only the interface properties will be validated).";
                try {
                    Debug.WriteLine(msg);
                    if (Debugger.IsAttached)
                        Console.Error.WriteLine(msg);
                } catch (Exception) {
                    //swallow
                }

                throw new JsonSettingsException(msg);
            }
        }
    }

    public static class JsonSettingsAutosaveExtensions {
        // ReSharper disable once FieldCanBeMadeReadOnly.Global
        public static ProxyGenerationOptions Options;

        private static ProxyGenerator _generator;

        static JsonSettingsAutosaveExtensions() {
            Options = new ProxyGenerationOptions();
            Options.AdditionalAttributes.Add(new CustomAttributeInfo(typeof(ProxyGeneratedAttribute).GetConstructor(Array.Empty<Type>()), Array.Empty<object>()));
        }

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
            TypeValidation<TSettings>.ValidateAllVirtual();

            _generator ??= new ProxyGenerator();

            return _generator.CreateClassProxyWithTarget<TSettings>(settings, Options ?? ProxyGenerationOptions.Default, ApplicableInterceptors(settings).ToArray());
        }

        /// <summary>
        ///     Enables automatic saving when changing any <b>virtual properties</b> returning the interface of <typeparamref name="ISettings"/>.
        /// </summary>
        /// <typeparam name="ISettings">An interface, your <see cref="JsonSettings"/> is implementing</typeparam>
        /// <typeparam name="TSettings">The JsonSettings type, aka proxy victim</typeparam>
        /// <param name="settings">The settings class to wrap in a proxy.</param>
        /// <returns>The interface specified which will save on every set</returns>
        /// <exception cref="JsonSettingsException">When <typeparamref name="TSettings"/> has no virtual properties.</exception>
        public static ISettings EnableIAutosave<TSettings, ISettings>(this TSettings settings) where TSettings : JsonSettings, ISettings where ISettings : class {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (!typeof(ISettings).IsInterface)
                throw new ArgumentNullException(nameof(settings), "Target Type must be interface");
            TypeValidation<TSettings>.InterfaceTypeValidation<ISettings>.ValidateInterfaceVirtual();

            _generator ??= new ProxyGenerator();
            return _generator.CreateInterfaceProxyWithTarget<ISettings>((ISettings) (object) settings, Options ?? ProxyGenerationOptions.Default, ApplicableInterceptors(settings).ToArray());
        }

        public static IEnumerable<IInterceptor> ApplicableInterceptors<TSettings>(this TSettings settings) where TSettings : JsonSettings {
            //if it doesn't contain any virtual methods, throw for the developer to know about it.
            IInterceptor interceptor;
            if (settings is NotifiyingJsonSettings notifiying)
                interceptor = new JsonSettingsAutosaveNotificationInterceptor(settings, new NotificationBinder(notifiying));
            else
                interceptor = new JsonSettingsAutosaveInterceptor(settings);

            yield return interceptor;
        }

        /// <summary>
        ///     Suspends auto-saving until SuspendAutosave.Dispose or SuspendAutosave.Resume are called.<br/>
        ///     If changes are introduced while suspension then a save will be commited and resume or disposal.
        /// </summary>
        /// <returns>A suspend state tracker that can be Disposed for a using block</returns>
        public static SuspendAutosave SuspendAutosave<TSettings>(this TSettings settings) where TSettings : JsonSettings {
            return settings.Modulation.GetModule<AutosaveModule>().SuspendAutosave();
        }
    }
}