// This project deliberately does NOT set <Nullable>enable</Nullable> -- doing so project-wide
// is deferred work with its own warning wave (see the note in Directory.Build.targets). This
// file moved here from the base JsonSettings project, which DOES enable nullable, so its
// members carry `?` annotations. Turn on just the annotation context for this one file so the
// annotations stay legal without opting the project into flow analysis. Without it the
// compiler raises CS8632, which TreatWarningsAsErrors promotes to a build-breaking error
// (CS8632 is absent from the WarningsNotAsErrors allowlist in Directory.Build.targets).
#nullable enable annotations

using System;
using System.Collections.Generic;
using Nucs.JsonSettings.Modulation;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     The module <c>EnableAutosave()</c> attaches to a woven settings instance: the shared
    ///     suspension state of <see cref="SuspensionModule"/> plus the parts only the woven path
    ///     needs -- the monitored-property set the woven advice consults, and the lifetime slot
    ///     for the <see cref="NotificationBinder"/>.
    /// </summary>
    /// <remarks>
    ///     Lives in this package -- where it is actually wired up -- rather than in the base
    ///     package, which keeps only the neutral <see cref="SuspensionModule"/> primitive.
    ///     <see cref="SettingsBag"/> attaches its own <see cref="SettingsBagAutosaveModule"/>, so
    ///     this type never appears on a bag and the <c>is AutosaveModule</c> resolution scans in
    ///     <see cref="AutosaveRuntime"/> and <see cref="NotificationBinder"/> match woven modules
    ///     only. The gates, the suspension machine and the repopulate-driven loading bracket are
    ///     all inherited; suspension reaches either module kind through the shared base (see
    ///     <see cref="SuspendAutosave"/>).
    /// </remarks>
    public class AutosaveModule : SuspensionModule {
        /// <summary>
        ///     The property names a write to which commits a save, resolved once when autosave is
        ///     enabled.
        /// </summary>
        /// <remarks>
        ///     This used to be computed in the interceptor's constructor, which only existed
        ///     because a proxy existed. Weaving has no interceptor to hang it on, and the woven
        ///     advice must not pay for reflection on every single property write, so the set is
        ///     resolved once here and consulted as a hash lookup thereafter.
        ///
        ///     Null means "autosave was attached without a property filter" -- nothing is
        ///     monitored -- rather than "everything is monitored", so a module that somehow
        ///     reaches the advice half-initialized stays silent instead of saving on every write.
        /// </remarks>
        private HashSet<string>? _monitoredProperties;

        /// <summary>
        ///     Records which properties this module saves on. Called by EnableAutosave.
        /// </summary>
        internal void SetMonitoredProperties(HashSet<string> monitored) {
            _monitoredProperties = monitored;
        }

        /// <summary>
        ///     Whether a write to <paramref name="propertyName"/> should commit a save.
        /// </summary>
        public bool IsMonitored(string propertyName) {
            return _monitoredProperties != null && _monitoredProperties.Contains(propertyName);
        }

        /// <summary>
        ///     The notification handler taking care of binding and unbinding to property and collection changes.
        /// </summary>
        /// <remarks>
        ///     A pure lifetime slot: <c>EnableAutosave()</c> parks the <see cref="NotificationBinder"/>
        ///     here so this module disposes it when it is torn down. Nothing reaches through it any
        ///     more -- the binder subscribes to the settings' repopulate events itself, where the
        ///     load pipeline used to pattern-match this property for a resync interface -- which is
        ///     why <see cref="IDisposable"/> is now the honest type rather than a compatibility
        ///     compromise.
        /// </remarks>
        public IDisposable? NotificationsHandler { get; set; }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);
            NotificationsHandler?.Dispose();
        }
    }
}
