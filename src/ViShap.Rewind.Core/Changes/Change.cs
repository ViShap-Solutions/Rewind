namespace ViShap.Rewind;

/// <summary>One tracked value that a committed scope changed.</summary>
/// <remarks>
/// For a <see cref="RewindCell{T}"/>, <see cref="Owner"/> is the cell and <see cref="Member"/> is <see langword="null"/>.
/// For a property of a <see cref="RewindableAttribute"/> class, <see cref="Owner"/> is the instance and
/// <see cref="Member"/> the property name. For a Rewind collection, <see cref="Owner"/> is the collection, and
/// <see cref="Member"/>, <see cref="OldValue"/> and <see cref="NewValue"/> are <see langword="null"/>: the change states
/// that its content changed.
/// </remarks>
public sealed class Change
{
    internal Change(object owner, string? member, object? oldValue, object? newValue)
    {
        Owner = owner;
        Member = member;
        OldValue = oldValue;
        NewValue = newValue;
    }

    /// <summary>Gets the object that holds the changed value: a cell, an instance of a rewindable class, or a collection.</summary>
    public object Owner { get; }

    /// <summary>Gets the name of the changed property, or <see langword="null"/> for a cell or a collection.</summary>
    public string? Member { get; }

    /// <summary>Gets the value when the scope opened, or <see langword="null"/> for a collection.</summary>
    public object? OldValue { get; }

    /// <summary>Gets the value when the scope committed, or <see langword="null"/> for a collection.</summary>
    public object? NewValue { get; }
}
