#if NET40
using System;
using System.Collections.Generic;
using System.Linq;
using ReadOnlyCollectionsExtensions.Wrappers;

namespace ReadOnlyCollectionsExtensions {
    /// <summary>
    /// Extension methods around read-only collection interfaces
    /// </summary>
    public static class ExtensionMethods {
        /// <summary>
        /// Views an <see cref="ICollection{T}"/> as a read-only collection
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <returns></returns>
        public static IReadOnlyCollection<T> AsReadOnlyCollection<T>(this ICollection<T> source) {
            if (source == null)
                return null;
            return new ReadOnlyCollectionWrapper<T>(source);
        }

        /// <summary>
        /// View an <see cref="IList{T}"/> as a read-only list
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <returns></returns>
        public static IReadOnlyList<T> AsReadOnlyList<T>(this IList<T> source) {
            if (source == null)
                return null;
            return new ReadOnlyListWrapper<T>(source);
        }

        public static IReadOnlyList<T> AsReadOnlyList<T>(this ArraySegment<T> source) {
#if NET45
            return source;
#else
            return new ArraySegmentWrapper<T>(source);
#endif
        }

        /// <summary>
        /// Creates a new read-only list from an <see cref="IEnumerable{T}"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <returns></returns>
        public static IReadOnlyList<T> ToReadOnlyList<T>(this IEnumerable<T> source) {
            return source.ToList().AsReadOnlyList();
        }

        /// <summary>
        /// Creates a new read-only dictionary from an <see cref="IEnumerable{T}"/>
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="source"></param>
        /// <param name="keySelector"></param>
        /// <param name="valueSelector"></param>
        /// <param name="comparer"></param>
        /// <returns></returns>
        public static IReadOnlyDictionary<TKey, TValue> ToReadOnlyDictionary<TSource, TKey, TValue>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TValue> valueSelector, IEqualityComparer<TKey> comparer) {
            return new ReadOnlyDictionaryWrapper<TKey, TValue>(source.ToDictionary(keySelector, valueSelector, comparer));
        }

        /// <summary>
        /// Creates a new read-only dictionary from an <see cref="IEnumerable{T}"/>
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="source"></param>
        /// <param name="keySelector"></param>
        /// <param name="valueSelector"></param>
        /// <returns></returns>
        public static IReadOnlyDictionary<TKey, TValue> ToReadOnlyDictionary<TSource, TKey, TValue>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TValue> valueSelector) {
            return new ReadOnlyDictionaryWrapper<TKey, TValue>(source.ToDictionary(keySelector, valueSelector));
        }

        /// <summary>
        /// Creates a new read-only dictionary from a list of <see cref="KeyValuePair{K,V}"/>
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="dict"></param>
        /// <returns></returns>
        public static IReadOnlyDictionary<TKey, TValue> ToReadOnlyDictionary<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> dict) {
            return new ReadOnlyDictionaryWrapper<TKey, TValue>(dict.ToDictionary(x => x.Key, x => x.Value));
        }

        /// <summary>
        /// Creates a new read-only dictionary from a list of <see cref="KeyValuePair{K,V}"/>
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="dict"></param>
        /// <param name="comparer"></param>
        /// <returns></returns>
        public static IReadOnlyDictionary<TKey, TValue> ToReadOnlyDictionary<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> dict, IEqualityComparer<TKey> comparer) {
            return new ReadOnlyDictionaryWrapper<TKey, TValue>(dict.ToDictionary(x => x.Key, x => x.Value, comparer));
        }

        /// <summary>
        /// Views a <see cref="IDictionary{K,V}"/> as a read-only dictionary
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="dict"></param>
        /// <returns></returns>
        public static IReadOnlyDictionary<TKey, TValue> AsReadOnlyDictionary<TKey, TValue>(this IDictionary<TKey, TValue> dict) {
            if (dict == null)
                return null;
            return new ReadOnlyDictionaryWrapper<TKey, TValue>(dict);
        }
    }
}
#endif