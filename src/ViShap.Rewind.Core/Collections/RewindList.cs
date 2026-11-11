using System.Collections;

namespace ViShap.Rewind;

/// <summary>
/// A list whose mutations made while a scope is open are undone when that scope rolls back. Each mutation records
/// its inverse; a rollback replays the inverses newest first.
/// </summary>
/// <typeparam name="T">
/// The type of the elements. For a reference type only membership and position are restored: changes made inside an
/// element are not undone.
/// </typeparam>
/// <remarks>
/// A mutation with no scope open is a plain mutation. A list is not safe for concurrent mutation from several threads.
/// Mutating the list while enumerating it, including by a rollback, makes the enumeration throw
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class RewindList<T> : IList<T>, IReadOnlyList<T>
{
    private List<T> _items;
    private int _version;

    /// <summary>Creates an empty list.</summary>
    public RewindList()
    {
        _items = [];
    }

    /// <summary>Creates a list holding the elements of <paramref name="collection"/>, in order.</summary>
    /// <param name="collection">The elements to start with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="collection"/> is <see langword="null"/>.</exception>
    public RewindList(IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = [.. collection];
    }

    /// <summary>Gets the number of elements.</summary>
    public int Count => _items.Count;

    bool ICollection<T>.IsReadOnly => false;

    /// <summary>Gets or sets the element at <paramref name="index"/>. Setting it while a scope is open records the previous element.</summary>
    /// <param name="index">The zero-based position.</param>
    /// <returns>The element at <paramref name="index"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not less than <see cref="Count"/>.</exception>
    public T this[int index]
    {
        get => _items[index];
        set
        {
            var previous = _items[index];
            _items[index] = value;
            _version++;
            ScopeContext.Current?.Journal.Add(new SetEntry(this, index, previous));
        }
    }

    /// <summary>Adds <paramref name="item"/> at the end.</summary>
    /// <param name="item">The element to add.</param>
    public void Add(T item)
    {
        _items.Add(item);
        _version++;
        ScopeContext.Current?.Journal.Add(new RemoveRangeEntry(this, _items.Count - 1, 1));
    }

    /// <summary>Adds the elements of <paramref name="collection"/> at the end, in order. A rollback removes them with one entry.</summary>
    /// <param name="collection">The elements to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="collection"/> is <see langword="null"/>.</exception>
    public void AddRange(IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var start = _items.Count;
        _items.AddRange(collection);
        var added = _items.Count - start;
        if (added == 0)
        {
            return;
        }

        _version++;
        ScopeContext.Current?.Journal.Add(new RemoveRangeEntry(this, start, added));
    }

    /// <summary>Inserts <paramref name="item"/> at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based position the element takes.</param>
    /// <param name="item">The element to insert.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or greater than <see cref="Count"/>.</exception>
    public void Insert(int index, T item)
    {
        _items.Insert(index, item);
        _version++;
        ScopeContext.Current?.Journal.Add(new RemoveRangeEntry(this, index, 1));
    }

    /// <summary>Removes the element at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based position of the element to remove.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not less than <see cref="Count"/>.</exception>
    public void RemoveAt(int index)
    {
        var removed = _items[index];
        _items.RemoveAt(index);
        _version++;
        ScopeContext.Current?.Journal.Add(new InsertEntry(this, index, removed));
    }

    /// <summary>Removes the first occurrence of <paramref name="item"/>.</summary>
    /// <param name="item">The element to remove.</param>
    /// <returns><see langword="true"/> if the element was found and removed; otherwise <see langword="false"/>, and nothing is recorded.</returns>
    public bool Remove(T item)
    {
        var index = _items.IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Removes every element. While a scope is open the elements are not copied: the list detaches its storage into
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
            return;
        }

        scope.Journal.Add(new RestoreEntry(this, _items));
        _items = [];
    }

    /// <summary>Returns whether the list holds <paramref name="item"/>.</summary>
    /// <param name="item">The element to look for.</param>
    /// <returns><see langword="true"/> if the list holds the element.</returns>
    public bool Contains(T item) => _items.Contains(item);

    /// <summary>Returns the position of the first occurrence of <paramref name="item"/>.</summary>
    /// <param name="item">The element to look for.</param>
    /// <returns>The zero-based position, or -1 when the list does not hold the element.</returns>
    public int IndexOf(T item) => _items.IndexOf(item);

    /// <summary>Copies the elements to <paramref name="array"/>, starting at <paramref name="arrayIndex"/>.</summary>
    /// <param name="array">The destination.</param>
    /// <param name="arrayIndex">The position in <paramref name="array"/> of the first element copied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="array"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="array"/> is too short.</exception>
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <summary>Returns an enumerator over the elements, in order.</summary>
    /// <returns>An enumerator that throws <see cref="InvalidOperationException"/> if the list is mutated while it is in use.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates the elements of a <see cref="RewindList{T}"/>.</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly RewindList<T> _list;
        private readonly List<T> _items;
        private readonly int _version;
        private int _index;
        private T _current;

        internal Enumerator(RewindList<T> list)
        {
            _list = list;
            _items = list._items;
            _version = list._version;
            _index = 0;
            _current = default!;
        }

        /// <summary>Gets the element at the current position.</summary>
        public readonly T Current => _current;

        readonly object? IEnumerator.Current => _current;

        /// <summary>Advances to the next element.</summary>
        /// <returns><see langword="true"/> if there is a next element; <see langword="false"/> at the end.</returns>
        /// <exception cref="InvalidOperationException">The list was mutated after the enumerator was created.</exception>
        public bool MoveNext()
        {
            if (_version != _list._version)
            {
                throw new InvalidOperationException("The collection was modified; enumeration operation may not execute.");
            }

            if (_index < _items.Count)
            {
                _current = _items[_index++];
                return true;
            }

            _current = default!;
            return false;
        }

        /// <summary>Releases nothing; present for <see cref="IDisposable"/>.</summary>
        public readonly void Dispose()
        {
        }

        void IEnumerator.Reset()
        {
            if (_version != _list._version)
            {
                throw new InvalidOperationException("The collection was modified; enumeration operation may not execute.");
            }

            _index = 0;
            _current = default!;
        }
    }

    private sealed class SetEntry(RewindList<T> list, int index, T previous) : JournalEntry
    {
        public override void Undo()
        {
            list._items[index] = previous;
            list._version++;
        }
    }

    private sealed class RemoveRangeEntry(RewindList<T> list, int index, int count) : JournalEntry
    {
        public override void Undo()
        {
            list._items.RemoveRange(index, count);
            list._version++;
        }
    }

    private sealed class InsertEntry(RewindList<T> list, int index, T item) : JournalEntry
    {
        public override void Undo()
        {
            list._items.Insert(index, item);
            list._version++;
        }
    }

    private sealed class RestoreEntry(RewindList<T> list, List<T> items) : JournalEntry
    {
        public override void Undo()
        {
            list._items = items;
            list._version++;
        }
    }
}
