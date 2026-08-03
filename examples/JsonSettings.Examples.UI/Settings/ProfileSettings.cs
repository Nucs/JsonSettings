using Newtonsoft.Json;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.NotifyChanges;

namespace Nucs.JsonSettings.Examples.UI {
    /// <summary>
    ///     Dependent-property fan-out and the two independent opt-outs.
    ///     <list type="bullet">
    ///         <item><c>[NotifyChangesFor(nameof(FullName))]</c> keeps a computed, get-only
    ///         property's binding live: writing <see cref="First"/> raises
    ///         <c>PropertyChanged("First")</c> then <c>PropertyChanged("FullName")</c>.</item>
    ///         <item><c>[IgnoreNotify]</c> — <see cref="LastSavedBy"/> is persisted but never
    ///         notifies, so its binding deliberately goes stale.</item>
    ///         <item><c>[IgnoreAutosave]</c> — writing <see cref="SearchText"/> notifies (drives
    ///         the UI) but never <em>triggers</em> a save; persistence and notification opt-outs
    ///         are independent. Note the boundary: the attribute controls triggering, not
    ///         serialization, so its current value still rides along when another property saves
    ///         — add <c>[JsonIgnore]</c> as well to keep it out of the file entirely.</item>
    ///     </list>
    ///     The base also implements <c>INotifyPropertyChanging</c>, which the aspect raises before
    ///     each assignment — the activity log shows the old value captured on that edge.
    /// </summary>
    [Autosave, NotifyChanges]
    public class ProfileSettings : NotifiyingJsonSettings {
        public override string FileName { get; set; } = "ui.profile.json";

        [NotifyChangesFor(nameof(FullName))]
        public string First { get; set; } = "Ada";

        [NotifyChangesFor(nameof(FullName))]
        public string Last { get; set; } = "Lovelace";

        /// <summary>Computed and not persisted; it has no setter to weave, the fan-out feeds it.</summary>
        [JsonIgnore]
        public string FullName => $"{First} {Last}";

        /// <summary>Saved to disk, silent to bindings.</summary>
        [IgnoreNotify]
        public string LastSavedBy { get; set; }

        /// <summary>
        ///     UI state: drives bindings, never triggers a save. Its last value still serializes
        ///     when something else saves; add [JsonIgnore] too for a fully file-less property.
        /// </summary>
        [IgnoreAutosave]
        public string SearchText { get; set; }

        public ProfileSettings() { }
        public ProfileSettings(string fileName) : base(fileName) { }
    }
}
