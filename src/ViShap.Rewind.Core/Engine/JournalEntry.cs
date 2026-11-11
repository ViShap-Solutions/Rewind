namespace ViShap.Rewind.Engine;

/// <summary>One recorded change: how to undo it.</summary>
internal abstract class JournalEntry
{
    public abstract void Undo();
}

/// <summary>The value a cell held before its first write in a scope.</summary>
internal sealed class CellEntry<T>(RewindCell<T> cell, T previous) : JournalEntry
{
    public override void Undo() => cell.Restore(previous);
}

/// <summary>The compensation of an effect that has already happened.</summary>
internal sealed class CompensationEntry(Action undo) : JournalEntry
{
    public override void Undo() => undo();
}

/// <summary>The value a generated property held before its first write in a scope, and how to write it back.</summary>
internal sealed class PropertyEntry<TOwner, T>(TOwner owner, T previous, Action<TOwner, T> restore) : JournalEntry
    where TOwner : class
{
    public override void Undo() => restore(owner, previous);
}
