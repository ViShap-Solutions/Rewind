namespace ViShap.Rewind.Engine;

/// <summary>
/// The undo log shared by an outermost scope and every scope nested in it. A nested scope is a mark in it: rolling
/// the scope back undoes the entries above its mark, newest first.
/// </summary>
internal sealed class Journal
{
    private JournalEntry?[] _entries = new JournalEntry?[16];
    private int _count;

    public int Count => _count;

    public void Add(JournalEntry entry)
    {
        if (_count == _entries.Length)
        {
            Array.Resize(ref _entries, _count * 2);
        }

        _entries[_count++] = entry;
    }

    /// <summary>Undoes every entry above <paramref name="mark"/>, newest first; one that throws does not stop the others.</summary>
    public void RollbackTo(int mark, ref List<Exception>? failures)
    {
        for (var index = _count - 1; index >= mark; index--)
        {
            var entry = _entries[index]!;
            _entries[index] = null;
            _count = index;
            try
            {
                entry.Undo();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }
    }

    public void Clear()
    {
        Array.Clear(_entries, 0, _count);
        _count = 0;
    }
}
