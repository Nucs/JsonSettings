using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Nucs.JsonSettings.Examples {
    /// <summary>
    ///     A <see cref="JsonSettings"/> that implements <see cref="INotifyPropertyChanged"/> and
    ///     <see cref="INotifyPropertyChanging"/>.
    /// </summary>
    /// <remarks>Implementing this class instead of JsonSettings will bind All notification changes to trigger autosaving (not more than once a second).</remarks>
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