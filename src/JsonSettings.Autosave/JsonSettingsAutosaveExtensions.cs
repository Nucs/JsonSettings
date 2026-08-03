using System;
using System.Diagnostics;
using System.Reflection;
using Nucs.JsonSettings.Examples;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     Validates that a settings type was actually woven before autosave is enabled on it.
    /// </summary>
    /// <remarks>
    ///     The old implementation could not silently fail: if a class had no virtual properties,
    ///     Castle produced a proxy that ignored every write, so the library threw up front rather
    ///     than let a user believe their settings were being persisted. The failure mode under
    ///     weaving is different but just as quiet -- a class with no <see cref="AutosaveAttribute"/>
    ///     is simply never woven, so <c>EnableAutosave()</c> would attach a module that nothing
    ///     ever calls -- and it deserves the same treatment.
    /// </remarks>
    internal static class TypeValidation {
        public static void ValidateWoven(Type settingsType) {
            if (settingsType.GetCustomAttribute<AutosaveAttribute>(true) != null)
                return;

            var msg = $"JsonSettings: {settingsType.Name} is not marked with [Autosave], so its setters were never woven and "
                    + $"enabling autosave on it would silently do nothing. Add [Autosave] to the class:"
                    + Environment.NewLine + Environment.NewLine
                    + $"    [Autosave]" + Environment.NewLine
                    + $"    public class {settingsType.Name} : JsonSettings {{ ... }}"
                    + Environment.NewLine + Environment.NewLine
                    + $"[Autosave] is not inherited: weaving happens where a setter is declared, so every class in a settings "
                    + $"hierarchy that declares properties you want saved needs its own attribute. Note that properties no "
                    + $"longer need to be virtual -- that requirement belonged to the Castle.DynamicProxy implementation this "
                    + $"replaces.";
            try {
                if (Debugger.IsAttached)
                    Console.Error.WriteLine(msg);
            } catch (Exception) {
                //swallow
            }

            throw new JsonSettingsException(msg);
        }
    }

    public static class JsonSettingsAutosaveExtensions {
        /// <summary>
        ///     Enables automatic saving when changing any property of a class marked
        ///     <see cref="AutosaveAttribute"/>.
        /// </summary>
        /// <typeparam name="TSettings">A settings class implementing <see cref="JsonSettings"/></typeparam>
        /// <param name="settings">The settings instance to enable autosaving on.</param>
        /// <returns>
        ///     <paramref name="settings"/> itself. Unlike the previous Castle-based implementation
        ///     this is neither a proxy nor a copy -- the returned reference is the one that was
        ///     passed in, so every other reference to the same instance autosaves too.
        /// </returns>
        /// <exception cref="JsonSettingsException">When <typeparamref name="TSettings"/> is not marked <see cref="AutosaveAttribute"/>.</exception>
        public static TSettings EnableAutosave<TSettings>(this TSettings settings) where TSettings : JsonSettings {
            if (settings is null)
                throw new ArgumentNullException(nameof(settings));

            //SettingsBag has its own dictionary-backed autosave and is not woven. Its instance
            //EnableAutosave() hides this extension when called on a SettingsBag-typed reference, but
            //a JsonSettings-typed one resolves to the extension instead -- so without this the same
            //object would autosave through one reference and throw "not marked [Autosave]" through
            //another. Route it to the bag's own autosave so both behave identically.
            if (settings is SettingsBag bag) {
                bag.EnableAutosave();
                return settings;
            }

            //the concrete type, not TSettings: the caller may well hold a base-typed reference.
            var type = settings.GetType();
            TypeValidation.ValidateWoven(type);

            //Idempotent. Under Castle every call returned a fresh proxy, so calling twice simply
            //produced two proxies; here there is one instance and enabling twice would attach a
            //second AutosaveModule to it. The woven advice only ever consults the first module, so
            //the extra one would sit unused -- except on a NotifiyingJsonSettings, where it also
            //spins up a second NotificationBinder that subscribes to PropertyChanged and is never
            //disposed. Returning early keeps a repeated EnableAutosave() a harmless no-op.
            if (settings.Modulation.IsAttachedOfType<AutosaveModule>())
                return settings;

            var module = new AutosaveModule();
            module.SetMonitoredProperties(AutosaveRuntime.ResolveMonitoredProperties(type));
            if (settings is NotifiyingJsonSettings notifiying)
                module.NotificationsHandler = new NotificationBinder(notifiying);

            settings.Modulation.Attach(module);
            return settings;
        }

        /// <summary>
        ///     Enables automatic saving and returns the settings as <typeparamref name="ISettings"/>.
        /// </summary>
        /// <typeparam name="ISettings">An interface your <see cref="JsonSettings"/> class implements</typeparam>
        /// <typeparam name="TSettings">The JsonSettings type</typeparam>
        /// <param name="settings">The settings instance to enable autosaving on.</param>
        /// <returns>The instance, as the requested interface.</returns>
        /// <remarks>
        ///     Retained for source compatibility. Under Castle this built a genuinely different
        ///     object -- an interface proxy forwarding to the original -- which was the only way to
        ///     intercept a class whose properties were not virtual. Weaving rewrites the setters
        ///     themselves, so the interface buys nothing beyond the cast and
        ///     <see cref="EnableAutosave{TSettings}"/> is now equivalent and clearer.
        /// </remarks>
        /// <exception cref="JsonSettingsException">When <typeparamref name="TSettings"/> is not marked <see cref="AutosaveAttribute"/>.</exception>
        public static ISettings EnableIAutosave<TSettings, ISettings>(this TSettings settings) where TSettings : JsonSettings, ISettings where ISettings : class {
            if (settings is null)
                throw new ArgumentNullException(nameof(settings));
            if (!typeof(ISettings).IsInterface)
                throw new ArgumentException("Target type must be an interface", nameof(settings));

            return (ISettings) (object) settings.EnableAutosave();
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
