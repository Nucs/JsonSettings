using Nucs.JsonSettings.Modulation;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     The module <see cref="SettingsBag"/> attaches while its dictionary-backed autosave is
    ///     enabled: the neutral suspension state of <see cref="SuspensionModule"/> and nothing else.
    /// </summary>
    /// <remarks>
    ///     Its own type on purpose. The bag used to attach the woven path's <c>AutosaveModule</c>,
    ///     which pinned that type -- monitored-property set, notification-binder slot and all --
    ///     inside the base package even though the bag uses none of it. The bag's autosave is
    ///     driven entirely by <c>SettingsBag.TrySave</c> consulting the inherited gates, and by the
    ///     repopulate bracketing <see cref="SuspensionModule"/> wires up on attach; the woven
    ///     module now lives in <c>Nucs.JsonSettings.Autosave</c> and never appears on a bag, so
    ///     that package's <c>is AutosaveModule</c> resolution scans (the woven advice, the
    ///     notification binder) can never mistake a bag's module for a woven one. Its
    ///     <c>SuspendAutosave()</c> extension still drives this module -- it targets the shared
    ///     <see cref="SuspensionModule"/> base.
    /// </remarks>
    public class SettingsBagAutosaveModule : SuspensionModule { }
}
