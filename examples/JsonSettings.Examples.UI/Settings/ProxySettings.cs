using System.ComponentModel;
using Nucs.JsonSettings.NotifyChanges;

namespace Nucs.JsonSettings.Examples.UI {
    /// <summary>
    ///     Your own notifying base instead of <c>NotifiyingJsonSettings</c>. The raiser is named
    ///     <c>RaisePropertyChanged</c> — Prism's house style — and carries no JsonSettings-specific
    ///     plumbing; <c>[NotifyChanges]</c> finds it purely by convention (recognised names:
    ///     <c>OnPropertyChanged</c>, <c>RaisePropertyChanged</c>, <c>NotifyOfPropertyChange</c>).
    /// </summary>
    public abstract class BindableSettings : JsonSettings, INotifyPropertyChanged {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected BindableSettings() { }
        protected BindableSettings(string fileName) : base(fileName) { }
    }

    /// <summary>
    ///     Deliberately notify-only: <c>[NotifyChanges]</c> without <c>[Autosave]</c> and without
    ///     <c>EnableAutosave()</c>. Bindings refresh live on every keystroke while the file only
    ///     changes on an explicit <c>Save()</c> — proving the two packages are independent (a class
    ///     can notify without saving, or the reverse).
    /// </summary>
    [NotifyChanges]
    public class ProxySettings : BindableSettings {
        public override string FileName { get; set; } = "ui.proxy.json";

        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 8080;

        public ProxySettings() { }
        public ProxySettings(string fileName) : base(fileName) { }
    }
}
