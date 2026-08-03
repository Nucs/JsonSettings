using System.ComponentModel;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.NotifyChanges;

namespace Nucs.JsonSettings.Examples.UI {
    /// <summary>
    ///     The DI seam: consumers depend on this interface, not the concrete settings class. It
    ///     extends <see cref="INotifyPropertyChanged"/> so an interface-typed reference still binds.
    /// </summary>
    public interface IAppSettings : INotifyPropertyChanged {
        string DisplayName { get; set; }
        int LaunchCount { get; set; }
    }

    /// <summary>
    ///     Handed out as <see cref="IAppSettings"/> via
    ///     <c>EnableIAutosave&lt;AppSettings, IAppSettings&gt;()</c>. Since 2.2.0 that is the same
    ///     woven instance behind a cast — no interface proxy — so writes through the interface go
    ///     through the woven setters and keep saving and notifying.
    /// </summary>
    [Autosave, NotifyChanges]
    public class AppSettings : NotifiyingJsonSettings, IAppSettings {
        public override string FileName { get; set; } = "ui.app.json";

        public string DisplayName { get; set; } = "Examples.UI";
        public int LaunchCount { get; set; }

        public AppSettings() { }
        public AppSettings(string fileName) : base(fileName) { }
    }
}
