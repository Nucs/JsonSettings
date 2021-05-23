using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Nucs.JsonSettings.Examples;

namespace Nucs.JsonSettings.Autosave {
    /// <summary>
    ///     Takes care of binding nested objects that implement <see cref="INotifyCollectionChanged"/> and/or <see cref="INotifyPropertyChanged"/>. 
    /// </summary>
    [Serializable]
    public class NotificationBinder : IDisposable {
        private readonly NotifiyingJsonSettings _settings;
        private readonly HashSet<string> _properties;
        private readonly ConcurrentDictionary<string, (PropertyInfo Property, MethodInfo GetMethod, MethodInfo SetMethod, object CurrentValue)> _monitoredPropertiesTable;

        public NotificationBinder(NotifiyingJsonSettings settings) {
            _settings = settings;

            //populate information
            var monitoredProperties = _settings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                               .Where(p => p.GetSetMethod()?.IsVirtual == true
                                                           && p.GetCustomAttribute<JsonIgnoreAttribute>(true) == null
                                                           && p.GetCustomAttribute<IgnoreAutosaveAttribute>(true) == null
                                                           && AutosaveModule._frameworkParameters.All(f => f != p.Name))
                                               .ToArray();

            Dictionary<string, (PropertyInfo t, MethodInfo, MethodInfo, object)> dictionary = new Dictionary<string, (PropertyInfo t, MethodInfo, MethodInfo, object)>();
            foreach (var property in monitoredProperties)
                dictionary.Add(property.Name, (t: property, property.GetGetMethod(), property.GetSetMethod(), property.GetGetMethod().Invoke(_settings, null)));
            _monitoredPropertiesTable = new ConcurrentDictionary<string, (PropertyInfo Property, MethodInfo GetMethod, MethodInfo SetMethod, object CurrentValue)>(dictionary, StringComparer.Ordinal);
            _properties = new HashSet<string>(_monitoredPropertiesTable.Keys);

            //bind main event pipe
            _settings.PropertyChanged += OnPropertyChanged;

            //bind all already set fields
            foreach (var fieldInfo in settings.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)) {
                if (fieldInfo.GetValue(settings) is INotifyCollectionChanged collectionNotifiyible) {
                    collectionNotifiyible.CollectionChanged += SaveOnCollectionChanged;
                } else if (fieldInfo.GetValue(settings) is INotifyPropertyChanged notifiyible) {
                    notifiyible.PropertyChanged += SaveOnChange;
                }
            }
        }

        private void OnPropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (_monitoredPropertiesTable.TryGetValue(e.PropertyName, out (PropertyInfo Property, MethodInfo GetMethod, MethodInfo SetMethod, object CurrentValue) propInfo)) {
                var newValue = propInfo.GetMethod.Invoke(_settings, null);
                if (propInfo.CurrentValue != newValue) {
                    //save and persist the new value
                    SaveOnChange(sender, e);
                    _monitoredPropertiesTable[e.PropertyName] = (propInfo.Property, propInfo.GetMethod, propInfo.SetMethod, newValue);

                    if (newValue != null) {
                        //subscribe new object event
                        if (newValue is INotifyCollectionChanged collectionNotifiyible) {
                            collectionNotifiyible.CollectionChanged += SaveOnCollectionChanged;
                        } else if (newValue is INotifyPropertyChanged notifiyible) {
                            notifiyible.PropertyChanged += SaveOnChange;
                        }
                    }

                    //unsubscribe old event
                    if (propInfo.CurrentValue is INotifyCollectionChanged removeNotificationCollection) {
                        removeNotificationCollection.CollectionChanged -= SaveOnCollectionChanged;
                    } else if (propInfo.CurrentValue is INotifyPropertyChanged removeNotification) {
                        removeNotification.PropertyChanged -= SaveOnChange;
                    }
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
            _monitoredPropertiesTable.Clear();
        }

        #endregion

        public bool CanHandleProperty(string propName) {
            return _properties.Contains(propName);
        }
    }
}