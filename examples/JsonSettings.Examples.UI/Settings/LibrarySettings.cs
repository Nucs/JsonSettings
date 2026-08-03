using System.Collections.ObjectModel;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.NotifyChanges;

namespace Nucs.JsonSettings.Examples.UI {
    /// <summary>
    ///     Nested-collection autosave and burst coalescing. Because the base is
    ///     <c>NotifiyingJsonSettings</c>, <c>EnableAutosave()</c> attaches the
    ///     <c>NotificationBinder</c>: an in-place <c>Books.Add(...)</c> saves (nested
    ///     <c>CollectionChanged</c>), and replacing <see cref="Books"/> saves <em>and</em> rebinds
    ///     the new instance so later adds keep saving. <see cref="Zoom"/> is slider-bound to make
    ///     the write-per-tick burst visible; <c>SuspendAutosave()</c> collapses a known burst into
    ///     one save while notifications keep the UI live.
    /// </summary>
    [Autosave, NotifyChanges]
    public class LibrarySettings : NotifiyingJsonSettings {
        public override string FileName { get; set; } = "ui.library.json";

        public ObservableCollection<string> Books { get; set; } = new ObservableCollection<string> {
            "The Mythical Man-Month",
            "The Pragmatic Programmer",
        };

        public double Zoom { get; set; } = 1.0;

        public LibrarySettings() { }
        public LibrarySettings(string fileName) : base(fileName) { }
    }
}
