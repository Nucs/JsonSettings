using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.NotifyChanges;

namespace Nucs.JsonSettings.Examples.UI {
    /// <summary>
    ///     The flagship integration: the main window's chrome (position, size, title) is two-way
    ///     bound straight to this object, so moving or resizing the window IS the write that
    ///     persists it. <c>[Autosave]</c> saves each write, <c>[NotifyChanges]</c> raises
    ///     <c>PropertyChanged</c> so the bindings refresh, and the <c>NotifiyingJsonSettings</c>
    ///     base is what lets <c>EnableAutosave()</c> also watch nested notifiers (see
    ///     <see cref="LibrarySettings"/>). Close the app and reopen it — the window comes back
    ///     exactly where it was left, with zero window-state code.
    /// </summary>
    [Autosave, NotifyChanges]
    public class WindowSettings : NotifiyingJsonSettings {
        public override string FileName { get; set; } = "ui.window.json";

        public double Left { get; set; } = 160;
        public double Top { get; set; } = 120;
        public double Width { get; set; } = 1080;
        public double Height { get; set; } = 780;
        public string Title { get; set; } = "JsonSettings Examples.UI — every control is a bound settings property";

        public WindowSettings() { }
        public WindowSettings(string fileName) : base(fileName) { }
    }
}
