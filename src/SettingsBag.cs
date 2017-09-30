using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using nucs.Collections;
using Newtonsoft.Json;

namespace nucs.JsonSettings {
    public sealed class SettingsBag : JsonSettings {
        /// <summary>
        ///     Will perform a safe after a change in any non-hardcoded public property.
        /// </summary>
        public bool Autosave { get; set; } = false;

        [JsonIgnore]
        public override string FileName { get; set; }
        public SettingsBag() { }

        public SettingsBag(string fileName) {
            FileName = fileName;
#if NET
            foreach (var pi in this.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
#else
            foreach (var pi in this.GetType().GetTypeInfo().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
#endif
                if ((pi.CanRead && pi.CanWrite) == false)
                    continue;
                PropertyData.Add(pi.Name, pi);
            }
        }

        public object this[string key] {
            get {
                lock (this) return Get<object>(key);
            }
            set {
                lock (this) Set(key, value);
            }
        }

        public Dictionary<string, object> Data => _data;
        private readonly SafeDictionary<string, object> _data = new SafeDictionary<string, object>();
        private readonly SafeDictionary<string, PropertyInfo> PropertyData = new SafeDictionary<string, PropertyInfo>();

        public T Get<T>(string key, T @default = default(T)) {
            lock (this) {
                if (PropertyData.ContainsKey(key))
                    return (T) PropertyData[key].GetValue(this, null);

                var ret = _data[key];
                if (ret == null || ret.Equals(default(T)))
                    return @default;

                return (T) ret;
            }
        }

        public void Set(string key, object value) {
            lock (this) {
                if (PropertyData.ContainsKey(key))
                    PropertyData[key].SetValue(this, value, null);
                else
                    _data[key] = value;

                if (Autosave)
                    Save();
            }
        }

        public bool Remove(string key) {
            bool ret = false;
            lock (this)
                ret = _data.Remove(key);
            if (Autosave)
                Save();

            return ret;
        }

        public int Remove(Func<KeyValuePair<string, object>, bool> comprarer) {
            lock (this) {
                int ret = 0;
                foreach (var kv in _data.ToArray()) {
                    if (comprarer(kv))
                        if (Remove(kv.Key)) {
                            ret += 1;
                        }
                }
                return ret;
            }
        }

        public SettingsBag EnableAutosave() {
            Autosave = true;
            return this;
        }
    }
}