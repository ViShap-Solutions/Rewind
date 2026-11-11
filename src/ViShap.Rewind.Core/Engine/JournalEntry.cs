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
