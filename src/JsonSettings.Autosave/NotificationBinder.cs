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
    /// <remarks>
    ///     Attached by <c>EnableAutosave()</c> to any settings instance that itself implements
    ///     <see cref="INotifyPropertyChanged"/> -- the <see cref="NotifiyingJsonSettings"/> base, a
    ///     <c>[NotifyChangesMixin]</c>-woven class (whose interface exists only after the weave), or
    ///     a hand-written implementation. It was limited to the notifying BASE CLASS before 2.3.0,
    ///     which silently excluded mixin classes: their <c>ObservableCollection</c> properties
    ///     compiled, bound and looked alive, but an in-place Add/Remove never saved.
    /// </remarks>
    [Serializable]
    public class NotificationBinder : INotificationsHandler {
        private readonly JsonSettings _settings;

        //The same object as _settings, through the interface every subscription goes through. Kept
        //separately because the settings TYPE no longer proves the capability: the constructor
        //accepts any JsonSettings and asserts the interface at runtime, which is the only moment a
        //mixin-injected implementation is visible at all.
        private readonly INotifyPropertyChanged _notifier;
        private readonly HashSet<string> _properties;
        private readonly ConcurrentDictionary<string, (PropertyInfo Property, MethodInfo GetMethod, MethodInfo SetMethod, object CurrentValue)> _monitoredPropertiesTable;

        //The nested notifiers this binder has subscribed to, tracked so Dispose can unsubscribe
        //them. Without this, disposing the settings left these handlers live: mutating a
        //collection that had been bound would still call SaveOnCollectionChanged and save through a
        //disposed settings object, and the settings could never be collected.
        private readonly List<INotifyCollectionChanged> _boundCollections = new List<INotifyCollectionChanged>();
        private readonly List<INotifyPropertyChanged> _boundNotifiers = new List<INotifyPropertyChanged>();

        /// <summary>
        ///     Kept for source and binary compatibility with pre-2.3.0 callers; the notifying base
        ///     is just one of the shapes the <see cref="JsonSettings"/> overload accepts.
        /// </summary>
        public NotificationBinder(NotifiyingJsonSettings settings) : this((JsonSettings) settings) { }

        public NotificationBinder(JsonSettings settings) {
            if (!(settings is INotifyPropertyChanged notifier))
                throw new ArgumentException(
                    $"NotificationBinder requires a settings instance that implements INotifyPropertyChanged: "
                  + $"a NotifiyingJsonSettings base, a [NotifyChangesMixin]-woven class, or a hand-written "
                  + $"implementation. '{settings?.GetType().Name}' provides none of these, so there is no "
                  + $"PropertyChanged event to observe property replacements through.", nameof(settings));

            _settings = settings;
            _notifier = notifier;

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
            _notifier.PropertyChanged += OnPropertyChanged;

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
            if (e.PropertyName != null)
                RefreshBinding(e.PropertyName);
        }

        /// <summary>
        ///     Moves the nested-change subscriptions for one monitored property from the value bound
        ///     previously to the value the property holds now. Shared by the PropertyChanged pipe
        ///     above and by <see cref="Resync"/>; not a save, per the remarks on
        ///     <see cref="OnPropertyChanged"/>.
        /// </summary>
        private void RefreshBinding(string propertyName) {
            if (_monitoredPropertiesTable.TryGetValue(propertyName, out (PropertyInfo Property, MethodInfo GetMethod, MethodInfo SetMethod, object CurrentValue) propInfo)) {
                var newValue = ReflectionHelper.Getter(propInfo.Property)(_settings);
                if (propInfo.CurrentValue != newValue) {
                    _monitoredPropertiesTable[propertyName] = (propInfo.Property, propInfo.GetMethod, propInfo.SetMethod, newValue);
                    Subscribe(newValue);
                    Unsubscribe(propInfo.CurrentValue);
                }
            }
        }

        /// <summary>
        ///     Re-reads every monitored property and rebinds the ones whose value was replaced.
        /// </summary>
        /// <remarks>
        ///     The load pipeline calls this (through <see cref="INotificationsHandler"/>) after every
        ///     populate: collection properties deserialize with Replace semantics, and the replace the
        ///     deserializer performs only reaches <see cref="OnPropertyChanged"/> when the class
        ///     raises PropertyChanged from its setters. A plain [Autosave] class on the notifying
        ///     base without the [NotifyChanges] aspect raises nothing during a populate and would
        ///     otherwise be left subscribed to collections the settings object no longer holds --
        ///     every in-place edit after a Load() silently unpersisted.
        /// </remarks>
        public void Resync() {
            foreach (var propertyName in _properties)
                RefreshBinding(propertyName);
        }

        private void SaveOnChange(object sender, PropertyChangedEventArgs e) {
            CommitNestedChange();
        }

        private void SaveOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
            CommitNestedChange();
        }

        /// <summary>
        ///     Commits the save a nested change earned, honouring the same module gates the
        ///     woven-setter path honours in <see cref="AutosaveRuntime.OnPropertySet"/>.
        /// </summary>
        /// <remarks>
        ///     These handlers used to call <c>Save()</c> directly, which bypassed every gate the
        ///     documentation promises: a populate that mutated a bound nested object or filled a
        ///     get-only collection saved the half-loaded file from inside Load(); an AfterSave
        ///     handler that mutated a bound collection re-entered Save without bound; and an
        ///     in-place Add inside a SuspendAutosave() scope saved immediately instead of batching
        ///     into the resume commit. A nested change now takes the exact decision path a woven
        ///     setter write takes, so "loads do not autosave", "re-entrancy is safe" and
        ///     "suspension batches into one save" hold regardless of which pipe reported the
        ///     change.
        /// </remarks>
        private void CommitNestedChange() {
            var module = TryGetAutosaveModule();
            if (module is null) {
                //no module to consult - the binder is being used stand-alone, outside
                //EnableAutosave(); preserve the old direct behaviour.
                _settings.Save();
                return;
            }

            if (module.IsSaving)
                return; //re-entered from inside this module's own Save (an AfterSave handler
                        //mutating a bound collection); saving again would recurse forever

            if (module.IsLoading)
                return; //a populate is rewriting this instance; the values come from disk and
                        //must not be written back mid-load

            if (module.AutosavingState == AutosavingState.SuspendedChanged)
                return; //a save is already owed; nothing further to record

            if (module.UpdatesSuspended) {
                module.AutosavingState = AutosavingState.SuspendedChanged; //commit on resume
                return;
            }

            module.IsSaving = true;
            try {
                _settings.Save();
            } finally {
                module.IsSaving = false;
            }
        }

        /// <summary>
        ///     Resolves the attached <see cref="AutosaveModule"/>, or null when there is none;
        ///     the same direct scan as <c>AutosaveRuntime.TryGetAutosaveModule</c>, for the same
        ///     reason (GetModule throws on absence).
        /// </summary>
        private AutosaveModule TryGetAutosaveModule() {
            var modules = _settings.Modulation.Modules;
            var len = modules.Count;
            for (int i = 0; i < len; i++) {
                if (modules[i] is AutosaveModule module)
                    return module;
            }

            return null;
        }

        #region IDisposable

        public void Dispose() {
            _notifier.PropertyChanged -= OnPropertyChanged;

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