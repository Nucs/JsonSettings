using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Nucs.JsonSettings.Examples {
    /// <summary>
    ///     A <see cref="JsonSettings"/> that implements <see cref="INotifyPropertyChanged"/>.
    /// </summary>
    /// <remarks>Implementing this class instead of JsonSettings will bind All notification changes to trigger autosaving (not more than once a second).</remarks>
    public abstract class NotifiyingJsonSettings : JsonSettings, INotifyPropertyChanged {
        protected NotifiyingJsonSettings() { }
        protected NotifiyingJsonSettings(string fileName) : base(fileName) { }

        public event PropertyChangedEventHandler? PropertyChanged;

        public virtual void OnPropertyChanged([CallerMemberName] string propertyName = null!) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}