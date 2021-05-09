using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Collections;

namespace Nucs.JsonSettings {
    /// <summary>
    ///     A dynamic settings class, adds settings as you go.
    /// </summary>
    /// <remarks>SettingsBag is threadsafe by using <see cref="ConcurrentDictionary{TKey,TValue}"/>.</remarks>
    public sealed class SettingsBag : JsonSettings {
        private readonly SafeDictionary<string, object> _data = new SafeDictionary<string, object>();
        private readonly SafeDictionary<string, PropertyInfo> PropertyData = new SafeDictionary<string, PropertyInfo>();
        private AutosaveModule? _autosaveModule; //TODO: this potentially can support WPF binding

        /// <summary>
        ///     All the settings in this bag.
        /// </summary>
        public IReadOnlyDictionary<string, object> Data => _data;

        [JsonIgnore]
        public override string FileName { get; set; }

        /// <summary>
        ///     Enable autosave when a property is written.
        /// </summary>
        /// <returns></returns>
        public SettingsBag EnableAutosave() {
            Autosave = true;
            return this;
        }

        /// <summary>
        ///     Return a dynamic accessor that will accept any variable that can be serialized by <see cref="Newtonsoft.Json"/>.
        ///     Index access ([]) or Property/Field is working.
        /// </summary>
        /// <returns></returns>
        public dynamic AsDynamic() {
            return new DynamicSettingsBag(this);
        }

        private bool _autosave;

        /// <summary>
        ///     Will perform a safe after a change in any non-hardcoded public property.
        /// </summary>
        [JsonIgnore]
        public bool Autosave {
            get => _autosave;
            set {
                if (value == _autosave)
                    return;

                _autosave = value;

                if (value && _autosaveModule == null)
                    Modulation.Attach(_autosaveModule = new AutosaveModule());
                else if (!value && _autosaveModule != null) {
                    Modulation.Deattach(_autosaveModule);
                    _autosaveModule = null;
                }
            }
        }

        public SettingsBag() { }

        public SettingsBag(string fileName) {
            FileName = fileName;
            if (this.GetType() != typeof(SettingsBag))
                foreach (var pi in this.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                    if ((pi.CanRead && pi.CanWrite) == false)
                        continue;
                    PropertyData.Add(pi.Name, pi);
                }
        }

        public object this[string key] {
            get => Get<object>(key);
            set => Set(key, value);
        }

        /// <summary>
        ///     Gets the value corresponding to the given <paramref name="key"/> or returns <see cref="default(T)"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <param name="default"></param>
        /// <returns></returns>
        public T Get<T>(string key, T @default = default(T)) {
            if (PropertyData.TryGetValue(key, out var prop))
                return (T) prop.GetValue(this, null);

            if (_data.TryGetValue(key, out var value))
                return (T) value;

            return default;
        }

        /// <summary>
        ///     Sets or adds a value.
        /// </summary>
        public void Set(string key, object value) {
            if (PropertyData.TryGetValue(key, out var prop)) {
                prop.SetValue(this, value);
                TrySave();
                return;
            }
            
            _data[key] = value;
            TrySave();
        }

        public bool Remove(string key) {
            var ret = _data.TryRemove(key, out _);
            if (ret)
                TrySave();
            return ret;
        }

        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TrySave() {
            if (Autosave && _autosaveModule!.AutosavingState != AutosavingState.SuspendedChanged) {
                if (_autosaveModule.UpdatesSuspended) {
                    _autosaveModule.AutosavingState = AutosavingState.SuspendedChanged;
                } else
                    Save();
            }
        }

        /// <summary>
        ///     Removes all items that <paramref name="comprarer"/> returns true to. <Br></Br>
        ///     Remove where is similar to <see cref="List{T}.RemoveAll"/>.
        /// </summary>
        public int RemoveWhere(Func<KeyValuePair<string, object>, bool> comprarer) {
            int ret = 0;
            foreach (var kv in _data) {
                if (comprarer(kv))
                    if (_data.TryRemove(kv.Key, out _))
                        ret++;
            }

            if (ret > 0)
                TrySave();
            
            return ret;
        }
    }
}