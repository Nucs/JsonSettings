using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Nucs.JsonSettings.Autosave;

namespace Nucs.JsonSettings.Examples {
    static class INotifyPropertyChangedExample {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        private static NotifyingSettings Settings { get; set; }

        public static void Run(string[] args) {
            //simple app to open a file by command and browse to a new file on demand.
            Settings = JsonSettings.Load<NotifyingSettings>("observable.jsn").EnableAutosave();
            Settings.Residents.Add("Cookie Monster"); //Boom! saves.
            Settings.Residents = new ObservableCollection<string>(); //Boom! saves.
            Settings.Residents.Add("Cookie Monster"); //Boom! saves.
            Settings.NonAutosavingProperty = new ObservableCollection<object>(); //doesn't save
            Settings.NonAutosavingProperty.Add("Jim"); //doesn't save
            Settings.Street += "-1"; //Boom! saves.
            Settings.AutoProperty = "Hello"; //Boom! saves.
            Settings.IgnoredFromAutosaving = "Hello"; //doesn't save
        }
    }

    public class NotifyingSettings : NotifiyingJsonSettings {
        public override string FileName { get; set; } = "some.default.just.in.case.jsn";
        private string _street = "Sesamee Street 123";
        private ObservableCollection<object> _nonAutosavingProperty;
        private ObservableCollection<string> _residents = new ObservableCollection<string>();

        /// will not autosave because property is not virtual
        public ObservableCollection<object> NonAutosavingProperty {
            get => _nonAutosavingProperty;
            set {
                if (Equals(value, _nonAutosavingProperty)) return;
                _nonAutosavingProperty = value;
                OnPropertyChanged();
            }
        }

        public virtual string Street {
            get => _street;
            set {
                if (value == _street) return;
                _street = value;
                OnPropertyChanged();
            }
        }
        
        public virtual string AutoProperty {
            get;
            set;
        }


        [IgnoreAutosave]
        public virtual string IgnoredFromAutosaving {
            get;
            set;
        }


        public virtual ObservableCollection<string> Residents {
            get => _residents;
            set {
                if (Equals(value, _residents)) return;
                _residents = value;
                OnPropertyChanged();
            }
        }

        public NotifyingSettings() { }
        public NotifyingSettings(string fileName) : base(fileName) { }
    }
}