#if NET40
using System;

namespace System.Collections.Generic {
    /// <summary>
    /// Read-only collection of elements.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
#if NET40
    public interface IReadOnlyCollection<out T> : IEnumerable<T> {
#elif NET20 || NET35
    public interface IReadOnlyCollection<T> : IEnumerable<T> {
#endif
        /// <summary>
        /// Number of elements in the collection.
        /// </summary>
        int Count { get; }
    }

    /// <summary>
    /// Read-only list of elements.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
#if NET40
    public interface IReadOnlyList<out T> : IReadOnlyCollection<T> {
#elif NET20 || NET35
    public interface IReadOnlyList<T> : IReadOnlyCollection<T> {
#endif
        /// <summary>
        /// Gets the element at the specified index in the list.
        /// </summary>
        /// <param name="index">Zero-based index of the element to get.</param>
        /// <returns>Element at the specified index in the list.</returns>
        T this[int index] { get; }
    }

    /// <summary>
    /// Read-only collection of key/value pairs.
    /// </summary>
    /// <typeparam name="TKey">Key type.</typeparam>
    /// <typeparam name="TValue">Value type.</typeparam>
    public interface IReadOnlyDictionary<TKey, TValue> : IReadOnlyCollection<KeyValuePair<TKey, TValue>> {
        /// <summary>
        /// Gets the element that has the specified key in the dictionary.
        /// </summary>
        /// <param name="key">Key to locate.</param>
        /// <returns>Element with the specified key in the dictionary.</returns>
        TValue this[TKey key] { get; }

        /// <summary>
        /// Keys in the dictionary.
        /// </summary>
        IEnumerable<TKey> Keys { get; }

        /// <summary>
        /// Values in the dictionary.
        /// </summary>
        IEnumerable<TValue> Values { get; }

        /// <summary>
        /// Determines whether the dictionary contains an element with the specified key.
        /// </summary>
        /// <param name="key">Key to locate.</param>
        /// <returns>true if the dictionary contains the specified key; otherwise false.</returns>
        bool ContainsKey(TKey key);

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        /// <param name="key">Key to locate.</param>
        /// <param name="value">Value located, if successful.</param>
        /// <returns>true if the key was found; otherwise false.</returns>
        bool TryGetValue(TKey key, out TValue value);
    }
}
#endif