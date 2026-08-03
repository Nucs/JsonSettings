using System.Collections.ObjectModel;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.NotifyChanges;

namespace Nucs.JsonSettings.Examples.UI {
    /// <summary>
    ///     Written from a background thread to demonstrate
    ///     <c>EnableNotificationMarshaling()</c>: with it on, <c>PropertyChanged</c> (and the
    ///     dependents) post back to the UI thread's <c>SynchronizationContext</c>; with it off they
    ///     run inline on the worker. <c>PropertyChanging</c> is never marshalled — it must precede
    ///     the write — which the activity log makes visible by stamping each edge with its thread.
    ///     Autosave runs on the writing thread either way, so the saves here happen off the UI thread.
    /// </summary>
    [Autosave, NotifyChanges]
    public class WorkerSettings : NotifiyingJsonSettings {
        public override string FileName { get; set; } = "ui.worker.json";

        public string Status { get; set; } = "idle";
        public int Progress { get; set; }
        public ObservableCollection<string> Results { get; set; } = new ObservableCollection<string>();

        public WorkerSettings() { }
        public WorkerSettings(string fileName) : base(fileName) { }
    }
}
