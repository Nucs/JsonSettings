using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Nucs.JsonSettings {
    /// <summary>
    ///     A <see cref="JsonSettings"/> that implements <see cref="INotifyPropertyChanged"/> and
    ///     <see cref="INotifyPropertyChanging"/>.
    /// </summary>
    /// <remarks>
    ///     This base provides the notification interface that data binding and the autosave
    ///     package's nested-change binding key on: raise <see cref="OnPropertyChanged"/> from your
    ///     setters, or let the <c>[NotifyChanges]</c> aspect weave the raise in. Nothing autosaves
    ///     by itself -- call <c>EnableAutosave()</c> (Nucs.JsonSettings.Autosave), which attaches
    ///     the autosave module and, because this class implements the interface, a
    ///     NotificationBinder that also saves on nested collection/object changes. Every change
    ///     commits a save; there is no built-in throttling -- batch bursts of writes with
    ///     <c>SuspendAutosave()</c>.
    /// </remarks>
    public abstract class NotifiyingJsonSettings : JsonSettings, INotifyPropertyChanged, INotifyPropertyChanging {
        protected NotifiyingJsonSettings() { }
        protected NotifiyingJsonSettings(string fileName) : base(fileName) { }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        ///     Raised before a property changes. Fired by the <c>[NotifyChanges]</c> aspect (from the
        ///     <c>Nucs.JsonSettings.NotifyChanges</c> package) ahead of the assignment; raise it by hand
        ///     from a manual setter if you are not using the aspect.
        /// </summary>
        public event PropertyChangingEventHandler? PropertyChanging;

        public virtual void OnPropertyChanged([CallerMemberName] string propertyName = null!) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public virtual void OnPropertyChanging([CallerMemberName] string propertyName = null!) {
            PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
        }
    }
}