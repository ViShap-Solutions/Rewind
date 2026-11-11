namespace ViShap.Rewind;

/// <summary>
/// A unit of tracked state. A value written while a scope is open is restored when that scope rolls back.
/// </summary>
/// <typeparam name="T">
/// The type of the value. For a reference type only the reference is tracked: changes made inside the object it
/// points to are not undone, so an immutable type is the safe choice.
/// </typeparam>
/// <remarks>
/// A write with no scope open is a plain write. A cell is not safe for concurrent writes from several threads.
/// </remarks>
public sealed class RewindCell<T>
{
    private T _value;
    private long _epoch;

    /// <summary>Creates a cell holding the default value of <typeparamref name="T"/>.</summary>
    public RewindCell()
    {
        _value = default!;
    }

    /// <summary>Creates a cell holding <paramref name="value"/>.</summary>
    /// <param name="value">The initial value.</param>
    public RewindCell(T value)
    {
        _value = value;
    }

    /// <summary>Gets or sets the value. Setting it while a scope is open records the previous value for that scope's rollback.</summary>
    public T Value
    {
        get => _value;
        set
        {
            var scope = ScopeContext.Current;
            if (scope is not null && _epoch != scope.Epoch)
            {
                scope.Journal.Add(new CellEntry<T>(this, _value));
                _epoch = scope.Epoch;
            }

            _value = value;
        }
    }

    internal void Restore(T value) => _value = value;

    /// <summary>Returns the text of the current value.</summary>
    /// <returns>The result of the value's <see cref="object.ToString"/>, or an empty string when the value is <see langword="null"/>.</returns>
    public override string ToString() => _value?.ToString() ?? string.Empty;
}
