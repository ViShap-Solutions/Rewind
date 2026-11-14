using System.Collections;
using System.ComponentModel;

namespace ViShap.Rewind;

/// <summary>
/// A set whose mutations made while a scope is open are undone when that scope rolls back. Each mutation records its
/// inverse; a rollback replays the inverses newest first.
/// </summary>
/// <typeparam name="T">
/// The type of the elements. For a reference type only membership is restored: changes made inside an element are not
/// undone.
/// </typeparam>
/// <remarks>
/// A mutation with no scope open is a plain mutation. A set is not safe for concurrent mutation from several threads.
/// Mutating the set while enumerating it, including by a rollback, makes the enumeration throw
/// <see cref="InvalidOperationException"/>. A rollback restores membership; the order in which the set enumerates its
/// elements is not part of what it restores.
/// <para>
/// The set raises <see cref="PropertyChanged"/> for <c>Count</c>: at once for a mutation made with no scope open; once
/// the outermost scope commits or rolls back for a set a scope changed; nothing while a scope is open. A list for a UI
/// to show is a <see cref="RewindList{T}"/>, which also raises <c>CollectionChanged</c>.
/// </para>
/// </remarks>
public sealed class RewindSet<T> : ISet<T>, IReadOnlySet<T>, INotifyPropertyChanged, ICollectionNotifier
{
    private HashSet<T> _items;
    private int _version;

    /// <summary>
    /// Occurs when the set changed, for <c>Count</c>: at once for a mutation made with no scope open, and once the
    /// outermost scope that changed the set commits or rolls back.
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

    /// <summary>Creates an empty set that compares elements with the default equality comparer.</summary>
    public RewindSet()
        : this(comparer: null)
    {
    }

    /// <summary>Creates an empty set that compares elements with <paramref name="comparer"/>.</summary>
    /// <param name="comparer">The element comparer, or <see langword="null"/> for the default equality comparer.</param>
    public RewindSet(IEqualityComparer<T>? comparer)
    {
        _items = new HashSet<T>(comparer);
    }

    /// <summary>Creates a set holding the distinct elements of <paramref name="collection"/>, comparing them with the default equality comparer.</summary>
    /// <param name="collection">The elements to start with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="collection"/> is <see langword="null"/>.</exception>
    public RewindSet(IEnumerable<T> collection)
        : this(collection, comparer: null)
    {
    }

    /// <summary>Creates a set holding the distinct elements of <paramref name="collection"/>, comparing them with <paramref name="comparer"/>.</summary>
    /// <param name="collection">The elements to start with.</param>
    /// <param name="comparer">The element comparer, or <see langword="null"/> for the default equality comparer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="collection"/> is <see langword="null"/>.</exception>
    public RewindSet(IEnumerable<T> collection, IEqualityComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = new HashSet<T>(collection, comparer);
    }

    /// <summary>Gets the comparer that decides whether two elements are equal.</summary>
    public IEqualityComparer<T> Comparer => _items.Comparer;

    /// <summary>Gets the number of elements.</summary>
    public int Count => _items.Count;

    bool ICollection<T>.IsReadOnly => false;

    /// <summary>Adds <paramref name="item"/>.</summary>
    /// <param name="item">The element to add.</param>
    /// <returns><see langword="true"/> if the element was added; <see langword="false"/> if the set already held it, and nothing is recorded.</returns>
    public bool Add(T item)
    {
        if (!_items.Add(item))
        {
            return false;
        }

        _version++;
        if (ScopeContext.Current is { } scope)
        {
            Record(scope, new RemoveEntry(this, item));
        }
        else if (PropertyChanged is not null)
        {
            Raise();
        }

        return true;
    }

    /// <summary>Removes <paramref name="item"/>.</summary>
    /// <param name="item">The element to remove.</param>
    /// <returns><see langword="true"/> if the element was found and removed; otherwise <see langword="false"/>, and nothing is recorded.</returns>
    public bool Remove(T item)
    {
        if (!_items.Remove(item))
        {
            return false;
        }

        _version++;
        if (ScopeContext.Current is { } scope)
        {
            Record(scope, new AddEntry(this, item));
        }
        else if (PropertyChanged is not null)
        {
            Raise();
        }

        return true;
    }

    /// <summary>
    /// Removes every element. While a scope is open the elements are not copied: the set detaches its storage into
    /// the scope's journal and starts an empty one, and a rollback reattaches it.
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
                Raise();
            }

            return;
        }

        Record(scope, new RestoreEntry(this, _items));
        _items = new HashSet<T>(_items.Comparer);
    }

    void ICollectionNotifier.NotifyReset() => Raise();

    private void Raise()
    {
        PropertyChanged?.Invoke(this, CollectionNotifications.Count);
    }

    /// <summary>Adds every element of <paramref name="other"/> the set does not hold. A rollback removes exactly those, with one entry.</summary>
    /// <param name="other">The elements to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public void UnionWith(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        List<T>? added = null;
        foreach (var item in other)
        {
            if (_items.Add(item))
            {
                (added ??= []).Add(item);
            }
        }

        Changed(added, removed: null);
    }

    /// <summary>Removes every element of <paramref name="other"/> the set holds. A rollback adds exactly those back, with one entry.</summary>
    /// <param name="other">The elements to remove.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public void ExceptWith(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this))
        {
            Clear();
            return;
        }

        List<T>? removed = null;
        foreach (var item in other)
        {
            if (_items.Remove(item))
            {
                (removed ??= []).Add(item);
            }
        }

        Changed(added: null, removed);
    }

    /// <summary>Keeps only the elements that <paramref name="other"/> also holds. A rollback adds the removed ones back, with one entry.</summary>
    /// <param name="other">The elements to keep.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public void IntersectWith(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this) || _items.Count == 0)
        {
            return;
        }

        var keep = new HashSet<T>(other, _items.Comparer);
        List<T>? removed = null;
        foreach (var item in _items)
        {
            if (!keep.Contains(item))
            {
                (removed ??= []).Add(item);
            }
        }

        if (removed is not null)
        {
            foreach (var item in removed)
            {
                _items.Remove(item);
            }
        }

        Changed(added: null, removed);
    }

    /// <summary>
    /// Keeps the elements that either the set or <paramref name="other"/> holds, but not both. A rollback undoes exactly
    /// the additions and removals, with one entry.
    /// </summary>
    /// <param name="other">The elements to compare with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this))
        {
            Clear();
            return;
        }

        List<T>? added = null;
        List<T>? removed = null;
        foreach (var item in new HashSet<T>(other, _items.Comparer))
        {
            if (_items.Remove(item))
            {
                (removed ??= []).Add(item);
            }
            else
            {
                _items.Add(item);
                (added ??= []).Add(item);
            }
        }

        Changed(added, removed);
    }

    /// <summary>Returns whether the set holds <paramref name="item"/>.</summary>
    /// <param name="item">The element to look for.</param>
    /// <returns><see langword="true"/> if the set holds the element.</returns>
    public bool Contains(T item) => _items.Contains(item);

    /// <summary>Returns whether every element of the set is in <paramref name="other"/>.</summary>
    /// <param name="other">The elements to compare with.</param>
    /// <returns><see langword="true"/> if the set is a subset of <paramref name="other"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public bool IsSubsetOf(IEnumerable<T> other) => _items.IsSubsetOf(other);

    /// <summary>Returns whether every element of the set is in <paramref name="other"/>, which holds at least one more.</summary>
    /// <param name="other">The elements to compare with.</param>
    /// <returns><see langword="true"/> if the set is a proper subset of <paramref name="other"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public bool IsProperSubsetOf(IEnumerable<T> other) => _items.IsProperSubsetOf(other);

    /// <summary>Returns whether the set holds every element of <paramref name="other"/>.</summary>
    /// <param name="other">The elements to compare with.</param>
    /// <returns><see langword="true"/> if the set is a superset of <paramref name="other"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public bool IsSupersetOf(IEnumerable<T> other) => _items.IsSupersetOf(other);

    /// <summary>Returns whether the set holds every element of <paramref name="other"/> and at least one more.</summary>
    /// <param name="other">The elements to compare with.</param>
    /// <returns><see langword="true"/> if the set is a proper superset of <paramref name="other"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public bool IsProperSupersetOf(IEnumerable<T> other) => _items.IsProperSupersetOf(other);

    /// <summary>Returns whether the set and <paramref name="other"/> share at least one element.</summary>
    /// <param name="other">The elements to compare with.</param>
    /// <returns><see langword="true"/> if they share an element.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public bool Overlaps(IEnumerable<T> other) => _items.Overlaps(other);

    /// <summary>Returns whether the set and <paramref name="other"/> hold the same elements.</summary>
    /// <param name="other">The elements to compare with.</param>
    /// <returns><see langword="true"/> if they hold the same elements.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    public bool SetEquals(IEnumerable<T> other) => _items.SetEquals(other);

    /// <summary>Copies the elements to <paramref name="array"/>, starting at <paramref name="arrayIndex"/>.</summary>
    /// <param name="array">The destination.</param>
    /// <param name="arrayIndex">The position in <paramref name="array"/> of the first element copied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="array"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="array"/> is too short.</exception>
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <summary>Returns an enumerator over the elements.</summary>
    /// <returns>An enumerator that throws <see cref="InvalidOperationException"/> if the set is mutated while it is in use.</returns>
    public Enumerator GetEnumerator() => new(this);

    void ICollection<T>.Add(T item) => Add(item);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Changed(List<T>? added, List<T>? removed)
    {
        if (added is null && removed is null)
        {
            return;
        }

        _version++;
        if (ScopeContext.Current is { } scope)
        {
            Record(scope, new BulkEntry(this, added, removed));
        }
        else if (PropertyChanged is not null)
        {
            Raise();
        }
    }

    private void ThrowIfModified(int version)
    {
        if (version != _version)
        {
            throw new InvalidOperationException("The collection was modified; enumeration operation may not execute.");
        }
    }

    /// <summary>Enumerates the elements of a <see cref="RewindSet{T}"/>.</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly RewindSet<T> _set;
        private readonly int _version;
        private HashSet<T>.Enumerator _inner;

        internal Enumerator(RewindSet<T> set)
        {
            _set = set;
            _version = set._version;
            _inner = set._items.GetEnumerator();
        }

        /// <summary>Gets the element at the current position.</summary>
        public readonly T Current => _inner.Current;

        readonly object? IEnumerator.Current => _inner.Current;

        /// <summary>Advances to the next element.</summary>
        /// <returns><see langword="true"/> if there is a next element; <see langword="false"/> at the end.</returns>
        /// <exception cref="InvalidOperationException">The set was mutated after the enumerator was created.</exception>
        public bool MoveNext()
        {
            _set.ThrowIfModified(_version);
            return _inner.MoveNext();
        }

        /// <summary>Releases nothing; present for <see cref="IDisposable"/>.</summary>
        public readonly void Dispose()
        {
        }

        void IEnumerator.Reset()
        {
            _set.ThrowIfModified(_version);
            _inner = _set._items.GetEnumerator();
        }
    }

    private sealed class RemoveEntry(RewindSet<T> set, T item) : CollectionEntry(set)
    {
        public override void Undo()
        {
            set._items.Remove(item);
            set._version++;
        }

        public override void UndoRecorded() => set.Remove(item);
    }

    private sealed class AddEntry(RewindSet<T> set, T item) : CollectionEntry(set)
    {
        public override void Undo()
        {
            set._items.Add(item);
            set._version++;
        }

        public override void UndoRecorded() => set.Add(item);
    }

    private sealed class BulkEntry(RewindSet<T> set, List<T>? added, List<T>? removed) : CollectionEntry(set)
    {
        public override void Undo()
        {
            if (added is not null)
            {
                foreach (var item in added)
                {
                    set._items.Remove(item);
                }
            }

            if (removed is not null)
            {
                foreach (var item in removed)
                {
                    set._items.Add(item);
                }
            }

            set._version++;
        }

        public override void UndoRecorded()
        {
            Undo();
            set.RecordInCurrentScope(new BulkEntry(set, removed, added));
        }
    }

    private sealed class RestoreEntry(RewindSet<T> set, HashSet<T> items) : CollectionEntry(set)
    {
        public override void Undo()
        {
            set._items = items;
            set._version++;
        }

        public override void UndoRecorded()
        {
            set.RecordInCurrentScope(new RestoreEntry(set, set._items));
            set._items = items;
            set._version++;
        }
    }
}
