using ViShap.Rewind.Contracts;

namespace ViShap.Rewind.Engine;

/// <summary>One recorded change: how to undo it, and what it changed.</summary>
internal abstract class JournalEntry
{
    /// <summary>Gets the tracked object the entry changed, or <see langword="null"/> for a compensation.</summary>
    public virtual object? Owner => null;

    /// <summary>Gets what identifies the changed member within its owner: the property description, or <see langword="null"/>.</summary>
    public virtual object? Member => null;

    /// <summary>Gets the name of the changed member: the property name, or <see langword="null"/>.</summary>
    public virtual string? MemberName => null;

    /// <summary>Gets whether the change raises a notification after the outermost scope commits or rolls it back.</summary>
    public virtual bool Notifies => false;

    /// <summary>
    /// Gets whether the change is notified now, when the outermost scope ends: what <see cref="Notifies"/> said at the
    /// recording, or, for a collection, whether it has a subscriber now.
    /// </summary>
    public virtual bool NotifiesAtEnd => Notifies;

    /// <summary>Undoes the change without recording anything.</summary>
    public abstract void Undo();

    /// <summary>
    /// Undoes the change through the recording paths, so the current scope records how to redo it. A compensation does
    /// nothing: external effects are not replayed.
    /// </summary>
    public virtual void UndoRecorded()
    {
    }

    /// <summary>
    /// Describes the change from the value this entry recorded to the current one, or returns <see langword="null"/>
    /// when they are equal. Called on the oldest entry of an owner and member.
    /// </summary>
    public virtual Change? Describe() => null;

    /// <summary>Returns whether the current value differs from the value this entry recorded.</summary>
    public virtual bool Changed() => false;

    /// <summary>Raises the change notification of the member.</summary>
    public virtual void Notify()
    {
    }
}

/// <summary>The value a cell held before its first write in a scope.</summary>
internal sealed class CellEntry<T>(RewindCell<T> cell, T previous) : JournalEntry
{
    public override object Owner => cell;

    public override void Undo() => cell.Restore(previous);

    public override void UndoRecorded() => cell.Value = previous;

    public override bool Changed() => !EqualityComparer<T>.Default.Equals(previous, cell.Value);

    public override Change? Describe()
    {
        var current = cell.Value;
        return EqualityComparer<T>.Default.Equals(previous, current) ? null : new Change(cell, null, previous, current);
    }
}

/// <summary>The compensation of an effect that has already happened.</summary>
internal sealed class CompensationEntry(Action undo) : JournalEntry
{
    public override void Undo() => undo();
}

/// <summary>The value a generated property held before its first write in a scope.</summary>
internal sealed class PropertyEntry<TOwner, T>(TOwner owner, T previous, RewindPropertyInfo<TOwner, T> property) : JournalEntry
    where TOwner : class
{
    public override object Owner => owner;

    public override object Member => property;

    public override string MemberName => property.Name;

    public override bool Notifies => property.Notify is not null;

    public override void Undo() => property.Set(owner, previous);

    public override void UndoRecorded()
    {
        ScopeContext.Current?.Journal.Add(new PropertyEntry<TOwner, T>(owner, property.Get(owner), property));
        property.Set(owner, previous);
    }

    public override bool Changed() => !EqualityComparer<T>.Default.Equals(previous, property.Get(owner));

    public override Change? Describe()
    {
        var current = property.Get(owner);
        return EqualityComparer<T>.Default.Equals(previous, current) ? null : new Change(owner, property.Name, previous, current);
    }

    public override void Notify() => property.Notify?.Invoke(owner);
}

/// <summary>
/// A mutation of a Rewind collection: the collection is reported as a whole, and notified with a reset once the
/// outermost scope commits or rolls back.
/// </summary>
internal abstract class CollectionEntry(object collection) : JournalEntry
{
    public override object Owner => collection;

    public override bool NotifiesAtEnd => collection is ICollectionNotifier { Observed: true };

    public override bool Changed() => true;

    public override Change Describe() => new(collection, null, null, null);

    public override void Notify() => ((ICollectionNotifier)collection).NotifyReset();
}
