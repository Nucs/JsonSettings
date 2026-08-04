using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.NotifyChanges;

namespace Nucs.JsonSettings.Examples.UI {
    /// <summary>
    ///     The no-base path: a <c>sealed</c> class deriving plain <c>JsonSettings</c> with no
    ///     interface declared in source. <c>[NotifyChangesMixin]</c> injects
    ///     <c>INotifyPropertyChanged</c> and <c>INotifyPropertyChanging</c> at compile time and
    ///     raises them from every setter, so WPF binds to it directly and
    ///     <c>(object)settings is INotifyPropertyChanged</c> is <c>true</c> at runtime.
    ///     Subscribing from code needs a cast through <c>object</c>, because the compiler cannot
    ///     see the injected interface on a sealed class.
    /// </summary>
    [Autosave]
    [NotifyChangesMixin]
    public sealed class QuickSettings : JsonSettings {
        public override string FileName { get; set; } = "ui.quick.json";

        public string Endpoint { get; set; } = "https://localhost";
        public int Port { get; set; } = 5001;
        public bool UseTls { get; set; } = true;

        public QuickSettings() { }
        public QuickSettings(string fileName) : base(fileName) { }
    }
}
