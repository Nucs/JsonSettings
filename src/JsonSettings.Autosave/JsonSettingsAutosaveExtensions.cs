using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Nucs.JsonSettings.Examples;
using Nucs.JsonSettings.Modulation;

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
            if (settingsType.GetCustomAttribute<AutosaveAttribute>(true) == null) {
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

            //The attribute is necessary but not sufficient: it is plain metadata, present whether or
            //not AspectInjector actually ran, so it survives exactly the failure it should catch -- a
            //build that silently skipped the weave (a direct AspectInjector reference with
            //ExcludeAssets="build", a single-pass `msbuild -t:Restore;Build` that evaluated the
            //project before the package targets existed, AspectInjector_Enabled=false, a build
            //system that ignores NuGet .targets). The weave itself stamps the class with the empty
            //IAutosaveWoven mixin, so its absence here means the setters never got their advice and
            //"enabled" autosave would never write a byte.
            if (!JsonSettingsAutosaveExtensions.RequireWeaveMarker)
                return;
            if (typeof(IAutosaveWoven).IsAssignableFrom(settingsType))
                return;

            var unwoven = $"JsonSettings: {settingsType.Name} is marked [Autosave] but was never IL-woven -- the AspectInjector "
                        + $"build step did not run on the assembly that declares it, so its setters never call the autosave "
                        + $"runtime and enabling autosave would silently lose every change. Common causes: a direct "
                        + $"AspectInjector PackageReference with ExcludeAssets=\"build\" (or \"buildTransitive\"); building with "
                        + $"a single `msbuild -t:Restore;Build` invocation, which evaluates the project before the restored "
                        + $"package targets exist (use `msbuild -restore` or `dotnet build`); <AspectInjector_Enabled>false"
                        + $"</AspectInjector_Enabled>; or a build system that does not import NuGet build targets. Rebuild with "
                        + $"the weave enabled. If this assembly was woven by Nucs.JsonSettings.Autosave OLDER than 2.3.0 (which "
                        + $"stamped no marker) and cannot be rebuilt, set JsonSettingsAutosaveExtensions.RequireWeaveMarker = false.";
            try {
                if (Debugger.IsAttached)
                    Console.Error.WriteLine(unwoven);
            } catch (Exception) {
                //swallow
            }

            throw new JsonSettingsException(unwoven);
        }
    }

    public static class JsonSettingsAutosaveExtensions {
        /// <summary>
        ///     When true (the default), <see cref="EnableAutosave{TSettings}"/> refuses a settings
        ///     class that carries [Autosave] without carrying the <see cref="IAutosaveWoven"/> mixin
        ///     the weave stamps -- the signature of a build that silently skipped AspectInjector,
        ///     which would otherwise "enable" an autosave that never writes a byte.
        /// </summary>
        /// <remarks>
        ///     The one legitimate reason to turn this off: an assembly woven by
        ///     Nucs.JsonSettings.Autosave OLDER than 2.3.0 running against this version of the
        ///     runtime without a rebuild (a diamond dependency). Those assemblies are genuinely
        ///     woven -- the old aspect just stamped no marker -- and the advice they carry calls the
        ///     same <see cref="AutosaveRuntime.OnPropertySet"/> and works. Set this to false once at
        ///     startup for that mix; everything else about validation stays on.
        /// </remarks>
        public static bool RequireWeaveMarker { get; set; } = true;

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
            //the extra one would sit unused -- except on a notification-capable settings, where it
            //also spins up a second NotificationBinder that subscribes to PropertyChanged and is
            //never disposed. Returning early keeps a repeated EnableAutosave() a harmless no-op.
            if (settings.Modulation.IsAttachedOfType<AutosaveModule>())
                return settings;

            var module = new AutosaveModule();
            module.SetMonitoredProperties(AutosaveRuntime.ResolveMonitoredProperties(type));

            //Nested-change binding (ObservableCollections saving on in-place Add/Remove, nested
            //INotifyPropertyChanged objects saving on their own writes) requires the settings to
            //raise PropertyChanged so replacements can be re-bound -- so the gate is the INTERFACE,
            //however it got there: the NotifiyingJsonSettings base, a hand-written implementation,
            //or [NotifyChangesMixin], whose implementation exists only after the weave and which a
            //base-class test can never see. Testing the base class here is exactly how mixin
            //classes shipped with visibly bound, silently non-persisting collections.
            if (settings is INotifyPropertyChanged)
                module.NotificationsHandler = new NotificationBinder(settings);

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
        /// <remarks>
        ///     Resolves the shared <see cref="SuspensionModule"/> base, which finds whichever module
        ///     the instance carries: the woven path's <see cref="AutosaveModule"/> or the
        ///     <see cref="SettingsBagAutosaveModule"/> a <see cref="SettingsBag"/> attaches -- so a
        ///     bag suspends through this same extension without either package knowing the other's
        ///     concrete type.
        /// </remarks>
        public static SuspendAutosave SuspendAutosave<TSettings>(this TSettings settings) where TSettings : JsonSettings {
            return new SuspendAutosave(settings.Modulation.GetModule<SuspensionModule>());
        }
    }
}
