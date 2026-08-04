using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Nucs.JsonSettings.Examples;
using Nucs.JsonSettings.Reflection;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     Takes care of binding nested objects that implement <see cref="INotifyCollectionChanged"/> and/or <see cref="INotifyPropertyChanged"/>. 
    /// </summary>
    [Serializable]
    public class NotificationBinder : IDisposable {
        private readonly NotifiyingJsonSettings _settings;
        private readonly HashSet<string> _properties;
        private readonly ConcurrentDictionary<string, (PropertyInfo Property, MethodInfo GetMethod, MethodInfo SetMethod, object CurrentValue)> _monitoredPropertiesTable;

        //The nested notifiers this binder has subscribed to, tracked so Dispose can unsubscribe
        //them. Without this, disposing the settings left these handlers live: mutating a
        //collection that had been bound would still call SaveOnCollectionChanged and save through a
        //disposed settings object, and the settings could never be collected.
        private readonly List<INotifyCollectionChanged> _boundCollections = new List<INotifyCollectionChanged>();
        private readonly List<INotifyPropertyChanged> _boundNotifiers = new List<INotifyPropertyChanged>();

        public NotificationBinder(NotifiyingJsonSettings settings) {
            _settings = settings;

            //Watch every opted-in, readable property whose value may be a nested notifier. This is
            //broader than the save-on-assignment set (IsAutosaveMonitored): it deliberately keeps
            //get-only collections, which have no setter but whose contents must still save. It is
            //also narrower than the old field scan, which bound every private INotify* field
            //regardless of the property's [IgnoreAutosave] -- the source of the bug where an ignored
            //collection saved on mutation.
            var bindableProperties = _settings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                              .Where(AutosavePolicy.IsNotificationBindable)
                                              .ToArray();

            //ConcurrentDictionary, not Dictionary: this staging table is populated single-threaded
            //here, but keeping every map in the notification/autosave path concurrent removes the
            //plain-Dictionary failure mode outright (a torn bucket array / lost write under a racing
            //populate) rather than relying on the construction staying single-threaded forever. It
            //seeds _monitoredPropertiesTable, which is itself concurrent and mutated at runtime.
            ConcurrentDictionary<string, (PropertyInfo t, MethodInfo, MethodInfo, object)> dictionary = new ConcurrentDictionary<string, (PropertyInfo t, MethodInfo, MethodInfo, object)>(StringComparer.Ordinal);
            foreach (var property in bindableProperties) {
                var getter = property.GetGetMethod(true);
                //ReflectionHelper.Getter caches a compiled (or, under AOT, reflective) accessor per
                //property, shared with NotifyChangesRuntime; the MethodInfo is still carried in the
                //tuple for the record it keeps of each monitored property.
                dictionary[property.Name] = (t: property, getter, property.GetSetMethod(true), ReflectionHelper.Getter(property)(_settings));
            }
            _monitoredPropertiesTable = new ConcurrentDictionary<string, (PropertyInfo Property, MethodInfo GetMethod, MethodInfo SetMethod, object CurrentValue)>(dictionary, StringComparer.Ordinal);
            _properties = new HashSet<string>(_monitoredPropertiesTable.Keys);

            //bind main event pipe
            _settings.PropertyChanged += OnPropertyChanged;

            //bind the current value of each watched property
            foreach (var entry in _monitoredPropertiesTable.Values)
                Subscribe(entry.CurrentValue);
        }

        private void Subscribe(object value) {
            if (value is INotifyCollectionChanged collectionNotifiyible) {
                collectionNotifiyible.CollectionChanged += SaveOnCollectionChanged;
                _boundCollections.Add(collectionNotifiyible);
            } else if (value is INotifyPropertyChanged notifiyible) {
                notifiyible.PropertyChanged += SaveOnChange;
                _boundNotifiers.Add(notifiyible);
            }
        }

        private void Unsubscribe(object value) {
            if (value is INotifyCollectionChanged collectionNotifiyible) {
                collectionNotifiyible.CollectionChanged -= SaveOnCollectionChanged;
                _boundCollections.Remove(collectionNotifiyible);
            } else if (value is INotifyPropertyChanged notifiyible) {
                notifiyible.PropertyChanged -= SaveOnChange;
                _boundNotifiers.Remove(notifiyible);
            }
        }

        /// <summary>
        ///     Rebinds nested change notifications when a monitored property is replaced.
        /// </summary>
        /// <remarks>
        ///     This deliberately does NOT save. Under the Castle proxy the settings' own
        ///     PropertyChanged was the only signal that a hand-written setter had run, so this
        ///     handler had to commit the write itself. The woven advice now runs at the end of
        ///     every setter -- hand-written and auto-implemented alike -- so saving here as well
        ///     would commit twice for a single assignment.
        ///
        ///     What remains is the part the setter cannot do: swapping the CollectionChanged /
        ///     PropertyChanged subscriptions from the old nested object to the new one, so that
        ///     mutating a freshly assigned collection still saves.
        /// </remarks>
        private void OnPropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName != null && _monitoredPropertiesTable.TryGetValue(e.PropertyName, out (PropertyInfo Property, MethodInfo GetMethod, MethodInfo SetMethod, object CurrentValue) propInfo)) {
                var newValue = ReflectionHelper.Getter(propInfo.Property)(_settings);
                if (propInfo.CurrentValue != newValue) {
                    _monitoredPropertiesTable[e.PropertyName] = (propInfo.Property, propInfo.GetMethod, propInfo.SetMethod, newValue);
                    Subscribe(newValue);
                    Unsubscribe(propInfo.CurrentValue);
                }
            }
        }

        private void SaveOnChange(object sender, PropertyChangedEventArgs e) {
            _settings.Save();
        }

        private void SaveOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
            _settings.Save();
        }

        #region IDisposable

        public void Dispose() {
            _settings.PropertyChanged -= OnPropertyChanged;

            //unbind every nested notifier we subscribed to, so a collection held elsewhere cannot
            //keep saving through -- or keep alive -- a disposed settings object.
            foreach (var collection in _boundCollections.ToArray())
                collection.CollectionChanged -= SaveOnCollectionChanged;
            foreach (var notifier in _boundNotifiers.ToArray())
                notifier.PropertyChanged -= SaveOnChange;
            _boundCollections.Clear();
            _boundNotifiers.Clear();

            _monitoredPropertiesTable.Clear();
        }

        #endregion

        public bool CanHandleProperty(string propName) {
            return _properties.Contains(propName);
        }
    }
}