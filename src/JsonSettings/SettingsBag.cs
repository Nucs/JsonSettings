using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Nucs.JsonSettings.Autosave;
using Nucs.JsonSettings.Collections;

namespace Nucs.JsonSettings {
    /// <summary>
    ///     A dynamic settings class, adds settings as you go.
    /// </summary>
    /// <remarks>
    ///     The value store is thread-safe: it is backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>
    ///     (through <see cref="SafeDictionary{TKey,TValue}"/>), so concurrent <see cref="Get{T}"/>,
    ///     <see cref="Set"/>, <see cref="Remove"/> and enumeration of <see cref="Data"/> neither corrupt
    ///     state nor lose entries. Autosave-on-write (when <see cref="Autosave"/> is enabled) is a
    ///     separate concern: the persistence path is not fully synchronized across threads, so drive
    ///     saves from a single writer or coalesce a burst inside a SuspendAutosave scope.
    /// </remarks>
    public sealed class SettingsBag : JsonSettings {
        private readonly SafeDictionary<string, object> _data = new SafeDictionary<string, object>();
        private SettingsBagAutosaveModule? _autosaveModule; //TODO: this potentially can support WPF binding
        private bool _autosave;

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

                if (value && _autosaveModule is null)
                    Modulation.Attach(_autosaveModule = new SettingsBagAutosaveModule());
                else if (!value && _autosaveModule != null) {
                    Modulation.Deattach(_autosaveModule);
                    _autosaveModule = null;
                }
            }
        }

        public SettingsBag() { }

        public SettingsBag(string fileName) {
            FileName = fileName;
        }

        public object? this[string key] {
            get => Get<object>(key);
            set => Set(key, value);
        }

        /// <summary>
        ///     Gets the value for <paramref name="key"/>, converting it to <typeparamref name="T"/>, or
        ///     returns <paramref name="default"/> when the key is missing or its value is <c>null</c>.
        /// </summary>
        /// <remarks>
        ///     A value stored under one numeric width comes back under another after a round-trip --
        ///     Newtonsoft deserializes a JSON integer as <see cref="long"/>, so a value set as
        ///     <see cref="int"/> is a boxed <c>long</c> once reloaded. A hard <c>(T)</c> unbox threw
        ///     <see cref="InvalidCastException"/> on that, and a <c>null</c> value threw
        ///     <see cref="NullReferenceException"/>. This coerces through <see cref="Convert.ChangeType(object,System.Type)"/>
        ///     after the exact/assignable fast path, and treats a null the same as a missing key.
        /// </remarks>
        public T? Get<T>(string key, T @default = default(T)) {
            if (!_data.TryGetValue(key, out var value) || value is null)
                return @default;

            //Exact type, an assignable reference, or T == object: hand it back untouched.
            if (value is T typed)
                return typed;

            //Bridge numeric (and other IConvertible) width mismatches, e.g. the Int64 a JSON integer
            //deserializes back into for a Get<int>. Nullable is unwrapped so Get<int?> resolves too.
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            //Enums are not directly convertible from their underlying integral via Convert.ChangeType
            //(it throws InvalidCastException), yet a round-trip stores an enum exactly that way -- as the
            //boxed Int64 a JSON integer deserializes to, or as its name when a string-enum converter is
            //used. Coerce both forms back to the enum so Get<TEnum> survives a save/load like Get<int>.
            if (target.IsEnum) {
                return value is string enumName
                    ? (T) Enum.Parse(target, enumName, ignoreCase: true)
                    : (T) Enum.ToObject(target, Convert.ChangeType(value, Enum.GetUnderlyingType(target)));
            }

            return (T) Convert.ChangeType(value, target);
        }

        /// <summary>
        ///     Sets or adds a value.
        /// </summary>
        public void Set(string key, object value) {
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
            if (Autosave && !_autosaveModule!.IsLoading && _autosaveModule.AutosavingState != AutosavingState.SuspendedChanged) {
                if (_autosaveModule.UpdatesSuspended) {
                    _autosaveModule.AutosavingState = AutosavingState.SuspendedChanged;
                } else if (!_autosaveModule.IsSaving) {
                    //Same reentrancy guard the woven path uses: a write made from inside this Save
                    //(e.g. an AfterSave handler that touches the bag) must not re-enter and recurse
                    //until the stack overflows. The value is kept; it persists on the next save.
                    _autosaveModule.IsSaving = true;
                    try {
                        Save();
                    } finally {
                        _autosaveModule.IsSaving = false;
                    }
                }
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