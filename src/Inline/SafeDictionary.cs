using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Nucs.JsonSettings.Collections {
    /// <summary>
    ///     A dictionary that returns default(T) incase of not existing value.
    ///     And Add will add or set value.
    /// </summary>
    [DebuggerStepThrough]
    internal class SafeDictionary<TKey, TValue> : ConcurrentDictionary<TKey, TValue> {
        /// <summary>
        ///     Returns either the value or if not found - the default.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public new TValue? this[TKey key] {
            get {
                if (TryGetValue(key, out var val))
                    return val;
                return default;
            }
            set => base[key] = value!;
        }


        /// <summary>
        ///     Adds or sets the value to given key.
        /// </summary>
        public new void Add(TKey key, TValue value) {
            base[key] = value;
        }

        /// <summary>
        ///     Gets a value via iterating each.<br></br>If not found ,return default(TKey)
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public TKey FindKeyByValue(TValue value) {
            EqualityComparer<TValue> comparer = EqualityComparer<TValue>.Default;
            foreach (var pair in this)
                if (comparer.Equals(value, pair.Value)) return pair.Key;

            return default(TKey);
        }

        public SafeDictionary<TKey, TValue> Clone() {
            return new SafeDictionary<TKey, TValue>(this);
        }

        public new IEnumerable<TValue> Values() {
            return base.Values;
        }

        public new IEnumerable<TKey> Keys() {
            return base.Keys;
        }

        #region Constructors

        public SafeDictionary() { }
        public SafeDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection) : base(collection) { }
        public SafeDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer) : base(collection, comparer) { }
        public SafeDictionary(IEqualityComparer<TKey> comparer) : base(comparer) { }
        public SafeDictionary(int concurrencyLevel, IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer) : base(concurrencyLevel, collection, comparer) { }
        public SafeDictionary(int concurrencyLevel, int capacity) : base(concurrencyLevel, capacity) { }
        public SafeDictionary(int concurrencyLevel, int capacity, IEqualityComparer<TKey> comparer) : base(concurrencyLevel, capacity, comparer) { }

        #endregion
    }
}
