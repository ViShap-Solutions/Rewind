using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ViShap.Rewind;

/// <summary>
/// A dictionary whose mutations made while a scope is open are undone when that scope rolls back. Each mutation
/// records its inverse; a rollback replays the inverses newest first.
/// </summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TValue">
/// The type of the values. For a reference type only which value a key maps to is restored: changes made inside a
/// value are not undone.
/// </typeparam>
/// <remarks>
/// A mutation with no scope open is a plain mutation. A dictionary is not safe for concurrent mutation from several
/// threads. Mutating the dictionary while enumerating it, including by a rollback, makes the enumeration throw
/// <see cref="InvalidOperationException"/>. A rollback restores which keys are present and their values; the order in
/// which the dictionary enumerates them is not part of what it restores.
/// <para>
/// The dictionary raises <see cref="PropertyChanged"/> for <c>Count</c> when it changed and for the indexer,
/// <c>Keys</c> and <c>Values</c>: at once for a mutation made with no scope open; once the outermost scope commits or
/// rolls back for a dictionary a scope changed; nothing while a scope is open. A list for a UI to show is a
/// <see cref="RewindList{T}"/>, which also raises <c>CollectionChanged</c>.
/// </para>
/// </remarks>
public sealed class RewindDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>,
    INotifyPropertyChanged, ICollectionNotifier
    where TKey : notnull
{
    private Dictionary<TKey, TValue> _items;
    private int _version;

    /// <summary>
    /// Occurs when the dictionary changed, for <c>Count</c> when it changed and for the indexer, <c>Keys</c> and
    /// <c>Values</c>: at once for a mutation made with no scope open, and once the outermost scope that changed the
    /// dictionary commits or rolls back.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    bool ICollectionNotifier.Observed => Observed;

    /// <summary>Records <paramref name="entry"/> in <paramref name="scope"/>, and tells its journal there is a notification to raise when anything subscribes.</summary>
    private void Record(RewindScope scope, JournalEntry entry)
    {
        scope.Journal.Add(entry);
        if (Observed)
        {
            scope.Journal.MarkNotifying();
        }
    }

    private void RecordInCurrentScope(JournalEntry entry)
    {
        if (ScopeContext.Current is { } scope)
        {
            Record(scope, entry);
        }
    }

    private bool Observed => PropertyChanged is not null;

    /// <summary>Creates an empty dictionary that compares keys with the default equality comparer.</summary>
    public RewindDictionary()
        : this(comparer: null)
    {
    }

    /// <summary>Creates an empty dictionary that compares keys with <paramref name="comparer"/>.</summary>
    /// <param name="comparer">The key comparer, or <see langword="null"/> for the default equality comparer.</param>
    public RewindDictionary(IEqualityComparer<TKey>? comparer)
    {
        _items = new Dictionary<TKey, TValue>(comparer);
        Keys = new KeyView(this);
        Values = new ValueView(this);
    }

    /// <summary>Creates a dictionary holding the pairs of <paramref name="collection"/>, comparing keys with the default equality comparer.</summary>
    /// <param name="collection">The pairs to start with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="collection"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="collection"/> holds a key twice.</exception>
    public RewindDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection)
        : this(collection, comparer: null)
    {
    }

    /// <summary>Creates a dictionary holding the pairs of <paramref name="collection"/>, comparing keys with <paramref name="comparer"/>.</summary>
    /// <param name="collection">The pairs to start with.</param>
    /// <param name="comparer">The key comparer, or <see langword="null"/> for the default equality comparer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="collection"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="collection"/> holds a key twice.</exception>
    public RewindDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey>? comparer)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = new Dictionary<TKey, TValue>(collection, comparer);
        Keys = new KeyView(this);
        Values = new ValueView(this);
    }

    /// <summary>Gets the comparer that decides whether two keys are equal.</summary>
    public IEqualityComparer<TKey> Comparer => _items.Comparer;

    /// <summary>Gets the number of keys.</summary>
    public int Count => _items.Count;

    /// <summary>Gets a read-only view of the keys that follows the content of the dictionary.</summary>
    public ICollection<TKey> Keys { get; }

    /// <summary>Gets a read-only view of the values that follows the content of the dictionary.</summary>
    public ICollection<TValue> Values { get; }

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

    /// <summary>
    /// Gets the value of <paramref name="key"/>, or sets it. Setting it while a scope is open records the previous
    /// value, or that the key was absent.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The value of <paramref name="key"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">Getting a key the dictionary does not hold.</exception>
    public TValue this[TKey key]
    {
        get => _items[key];
        set
        {
            var existed = _items.TryGetValue(key, out var previous);
            _items[key] = value;
            _version++;
            var scope = ScopeContext.Current;
            if (scope is not null)
            {
                Record(scope, existed ? new SetEntry(this, key, previous!) : new RemoveEntry(this, key));
            }
            else if (PropertyChanged is not null)
            {
                Raise(countChanged: !existed);
            }
        }
    }

    /// <summary>Adds <paramref name="key"/> with <paramref name="value"/>.</summary>
    /// <param name="key">The key to add.</param>
    /// <param name="value">Its value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The dictionary already holds <paramref name="key"/>.</exception>
    public void Add(TKey key, TValue value)
    {
        _items.Add(key, value);
        Added(key);
    }

    /// <summary>Adds <paramref name="key"/> with <paramref name="value"/> unless the dictionary already holds the key.</summary>
    /// <param name="key">The key to add.</param>
    /// <param name="value">Its value.</param>
    /// <returns><see langword="true"/> if the key was added; otherwise <see langword="false"/>, and nothing is recorded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool TryAdd(TKey key, TValue value)
    {
        if (!_items.TryAdd(key, value))
        {
            return false;
        }

        Added(key);
        return true;
    }

    /// <summary>Removes <paramref name="key"/>.</summary>
    /// <param name="key">The key to remove.</param>
    /// <returns><see langword="true"/> if the key was found and removed; otherwise <see langword="false"/>, and nothing is recorded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool Remove(TKey key) => Remove(key, out _);

    /// <summary>Removes <paramref name="key"/> and returns its value.</summary>
    /// <param name="key">The key to remove.</param>
    /// <param name="value">The value the key had, or the default value when the key was absent.</param>
    /// <returns><see langword="true"/> if the key was found and removed; otherwise <see langword="false"/>, and nothing is recorded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (!_items.Remove(key, out value))
        {
            return false;
        }

        _version++;
        if (ScopeContext.Current is { } scope)
        {
            Record(scope, new AddEntry(this, key, value));
        }
        else if (PropertyChanged is not null)
        {
            Raise(countChanged: true);
        }

        return true;
    }

    /// <summary>
    /// Removes every key. While a scope is open the pairs are not copied: the dictionary detaches its storage into the
    /// scope's journal and starts an empty one, and a rollback reattaches it.
    /// </summary>
    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _version++;
        var scope = ScopeContext.Current;
        if (scope is null)
        {
            _items.Clear();
            if (PropertyChanged is not null)
            {
                Raise(countChanged: true);
            }

            return;
        }

        Record(scope, new RestoreEntry(this, _items));
        _items = new Dictionary<TKey, TValue>(_items.Comparer);
    }

    /// <summary>Returns whether the dictionary holds <paramref name="key"/>.</summary>
    /// <param name="key">The key to look for.</param>
    /// <returns><see langword="true"/> if the dictionary holds the key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool ContainsKey(TKey key) => _items.ContainsKey(key);

    /// <summary>Gets the value of <paramref name="key"/> if the dictionary holds it.</summary>
    /// <param name="key">The key to look for.</param>
    /// <param name="value">The value of the key, or the default value when the key is absent.</param>
    /// <returns><see langword="true"/> if the dictionary holds the key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => _items.TryGetValue(key, out value);

    /// <summary>Returns an enumerator over the pairs.</summary>
    /// <returns>An enumerator that throws <see cref="InvalidOperationException"/> if the dictionary is mutated while it is in use.</returns>
    public Enumerator GetEnumerator() => new(this);

    void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) =>
        _items.TryGetValue(item.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);

    bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) =>
        ((ICollection<KeyValuePair<TKey, TValue>>)this).Contains(item) && Remove(item.Key);

    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<TKey, TValue>>)_items).CopyTo(array, arrayIndex);

    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Added(TKey key)
    {
        _version++;
        if (ScopeContext.Current is { } scope)
        {
            Record(scope, new RemoveEntry(this, key));
        }
        else if (PropertyChanged is not null)
        {
            Raise(countChanged: true);
        }
    }

    void ICollectionNotifier.NotifyReset() => Raise(countChanged: true);

    private void Raise(bool countChanged)
    {
        if (countChanged)
        {
            PropertyChanged?.Invoke(this, CollectionNotifications.Count);
        }

        PropertyChanged?.Invoke(this, CollectionNotifications.Indexer);
        PropertyChanged?.Invoke(this, CollectionNotifications.Keys);
        PropertyChanged?.Invoke(this, CollectionNotifications.Values);
    }

    private void ThrowIfModified(int version)
    {
        if (version != _version)
        {
            throw new InvalidOperationException("The collection was modified; enumeration operation may not execute.");
        }
    }

    /// <summary>Enumerates the pairs of a <see cref="RewindDictionary{TKey, TValue}"/>.</summary>
    public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
    {
        private readonly RewindDictionary<TKey, TValue> _dictionary;
        private readonly int _version;
        private Dictionary<TKey, TValue>.Enumerator _inner;

        internal Enumerator(RewindDictionary<TKey, TValue> dictionary)
        {
            _dictionary = dictionary;
            _version = dictionary._version;
            _inner = dictionary._items.GetEnumerator();
        }

        /// <summary>Gets the pair at the current position.</summary>
        public readonly KeyValuePair<TKey, TValue> Current => _inner.Current;

        readonly object IEnumerator.Current => _inner.Current;

        /// <summary>Advances to the next pair.</summary>
        /// <returns><see langword="true"/> if there is a next pair; <see langword="false"/> at the end.</returns>
        /// <exception cref="InvalidOperationException">The dictionary was mutated after the enumerator was created.</exception>
        public bool MoveNext()
        {
            _dictionary.ThrowIfModified(_version);
            return _inner.MoveNext();
        }

        /// <summary>Releases nothing; present for <see cref="IDisposable"/>.</summary>
        public readonly void Dispose()
        {
        }

        void IEnumerator.Reset()
        {
            _dictionary.ThrowIfModified(_version);
            _inner = _dictionary._items.GetEnumerator();
        }
    }

    private abstract class View<T>(RewindDictionary<TKey, TValue> dictionary) : ICollection<T>, IReadOnlyCollection<T>
    {
        protected RewindDictionary<TKey, TValue> Dictionary => dictionary;

        public int Count => dictionary.Count;

        public bool IsReadOnly => true;

        public abstract bool Contains(T item);

        public void CopyTo(T[] array, int arrayIndex)
        {
            ArgumentNullException.ThrowIfNull(array);
            ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
            if (array.Length - arrayIndex < Count)
            {
                throw new ArgumentException("The destination array is too short.", nameof(array));
            }

            foreach (var item in this)
            {
                array[arrayIndex++] = item;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            var version = dictionary._version;
            foreach (var pair in dictionary._items)
            {
                dictionary.ThrowIfModified(version);
                yield return Select(pair);
            }

            dictionary.ThrowIfModified(version);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        void ICollection<T>.Add(T item) => throw new NotSupportedException("The view is read-only.");

        void ICollection<T>.Clear() => throw new NotSupportedException("The view is read-only.");

        bool ICollection<T>.Remove(T item) => throw new NotSupportedException("The view is read-only.");

        protected abstract T Select(KeyValuePair<TKey, TValue> pair);
    }

    private sealed class KeyView(RewindDictionary<TKey, TValue> dictionary) : View<TKey>(dictionary)
    {
        public override bool Contains(TKey item) => Dictionary.ContainsKey(item);

        protected override TKey Select(KeyValuePair<TKey, TValue> pair) => pair.Key;
    }

    private sealed class ValueView(RewindDictionary<TKey, TValue> dictionary) : View<TValue>(dictionary)
    {
        public override bool Contains(TValue item) => Dictionary._items.ContainsValue(item);

        protected override TValue Select(KeyValuePair<TKey, TValue> pair) => pair.Value;
    }

    private sealed class SetEntry(RewindDictionary<TKey, TValue> dictionary, TKey key, TValue previous) : CollectionEntry(dictionary)
    {
        public override void Undo()
        {
            dictionary._items[key] = previous;
            dictionary._version++;
        }

        public override void UndoRecorded() => dictionary[key] = previous;
    }

    private sealed class RemoveEntry(RewindDictionary<TKey, TValue> dictionary, TKey key) : CollectionEntry(dictionary)
    {
        public override void Undo()
        {
            dictionary._items.Remove(key);
            dictionary._version++;
        }

        public override void UndoRecorded() => dictionary.Remove(key);
    }

    private sealed class AddEntry(RewindDictionary<TKey, TValue> dictionary, TKey key, TValue value) : CollectionEntry(dictionary)
    {
        public override void Undo()
        {
            dictionary._items.Add(key, value);
            dictionary._version++;
        }

        public override void UndoRecorded() => dictionary.Add(key, value);
    }

    private sealed class RestoreEntry(RewindDictionary<TKey, TValue> dictionary, Dictionary<TKey, TValue> items) : CollectionEntry(dictionary)
    {
        public override void Undo()
        {
            dictionary._items = items;
            dictionary._version++;
        }

        public override void UndoRecorded()
        {
            dictionary.RecordInCurrentScope(new RestoreEntry(dictionary, dictionary._items));
            dictionary._items = items;
            dictionary._version++;
        }
    }
}
