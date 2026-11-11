namespace ViShap.Rewind;

/// <summary>
/// The tracked values an outermost scope and every scope nested in it changed, as they stood when it committed.
/// Delivered to the actions registered with <see cref="RewindScope.OnCommitted"/>.
/// </summary>
/// <remarks>
/// A value written and brought back to its starting value within the scope is not listed. Each changed value appears
/// once, in the order of its first change.
/// </remarks>
public sealed class ChangeSet
{
    internal ChangeSet(IReadOnlyList<Change> changes, JournalEntry[] entries)
    {
        Changes = changes;
        Entries = entries;
    }

    /// <summary>Gets the changed values, in the order of their first change.</summary>
    public IReadOnlyList<Change> Changes { get; }

    /// <summary>Gets whether the scope changed no tracked value.</summary>
    public bool IsEmpty => Changes.Count == 0;

    internal JournalEntry[] Entries { get; }

    /// <summary>Undoes every change, newest first, through the recording paths of the current scope.</summary>
    internal void UndoRecorded()
    {
        for (var index = Entries.Length - 1; index >= 0; index--)
        {
            Entries[index].UndoRecorded();
        }
    }
}
