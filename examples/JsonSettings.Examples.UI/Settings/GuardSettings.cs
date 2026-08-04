using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.NotifyChanges;

namespace Nucs.JsonSettings.Examples.UI {
    /// <summary>
    ///     One property per <see cref="NotificationGuard"/> flavour. The guard decides when a woven
    ///     setter <em>notifies</em>; autosave has no guard and persists every monitored write, so
    ///     the demo's save/notification counters diverge — that divergence is the lesson.
    /// </summary>
    [Autosave, NotifyChanges] //class default: NotificationGuard.OnlyChanged
    public class GuardSettings : NotifiyingJsonSettings {
        public override string FileName { get; set; } = "ui.guards.json";

        /// <summary>OnlyChanged (the class default): a same-value write saves but does not notify.</summary>
        public string Query { get; set; } = "initial query";

        /// <summary>Always: every setter access notifies, including a write of the current value.</summary>
        [NotifyChanges(Guard = NotificationGuard.Always)]
        public int RefreshTick { get; set; }

        /// <summary>
        ///     OnlyChanged | SkipNullOrDefault: notifies only on a change to a non-null value.
        ///     Clearing to null still saves null to disk, but raises nothing — so a binding
        ///     deliberately keeps showing the last non-null value.
        /// </summary>
        [NotifyChanges(Guard = NotificationGuard.OnlyChanged | NotificationGuard.SkipNullOrDefault)]
        public string Filter { get; set; } = "initial filter";

        public GuardSettings() { }
        public GuardSettings(string fileName) : base(fileName) { }
    }
}
