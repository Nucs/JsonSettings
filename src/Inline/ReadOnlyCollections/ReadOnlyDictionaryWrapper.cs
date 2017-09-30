#if NET40
using System;
using System.Collections;
using System.Collections.Generic;

namespace ReadOnlyCollectionsExtensions.Wrappers {
    public class ReadOnlyDictionaryWrapper<K, V> : IReadOnlyDictionary<K, V> {
        private readonly IDictionary<K, V> dict;

        public ReadOnlyDictionaryWrapper(IDictionary<K, V> dict) {
            if (dict == null)
                throw new ArgumentNullException("dict");
            this.dict = dict;
        }

        public IEnumerator<KeyValuePair<K, V>> GetEnumerator() {
            return dict.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }

        public int Count {
            get { return dict.Count; }
        }

        public V this[K key] {
            get { return dict[key]; }
        }

        public IEnumerable<K> Keys {
            get { return dict.Keys; }
        }

        public IEnumerable<V> Values {
            get { return dict.Values; }
        }

        public bool ContainsKey(K key) {
            return dict.ContainsKey(key);
        }

        public bool TryGetValue(K key, out V value) {
            return dict.TryGetValue(key, out value);
        }
    }
}
#endif