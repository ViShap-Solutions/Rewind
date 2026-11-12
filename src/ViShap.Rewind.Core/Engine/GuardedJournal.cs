namespace ViShap.Rewind.Engine;

/// <summary>
/// The journal of an asynchronous outermost scope, used by one flow at a time. It knows its innermost open scope, and
/// refuses a recording made through any other scope — a parallel branch, or a branch that outlived its scope — and a
/// recording made while another is in progress.
/// </summary>
internal sealed class GuardedJournal : Journal
{
    private const string NotInnermost = "Tracked state was written through an asynchronous scope that is not the innermost open scope of its journal: by a parallel branch of the flow, or after the scope closed. An asynchronous scope is used by one flow at a time; give each parallel branch its own outermost scope.";

    private const string Simultaneous = "Tracked state was written into one asynchronous scope from two threads at once. An asynchronous scope is used by one flow at a time; give each parallel branch its own outermost scope.";

    private int _recording;

    /// <summary>Gets or sets the innermost open scope, or <see langword="null"/> once the outermost one closed.</summary>
    public RewindScope? Top { get; set; }

    /// <summary>Marks a recording in progress; returns <see langword="false"/> when one already is.</summary>
    public bool TryEnterRecording() => Interlocked.Exchange(ref _recording, 1) == 0;

    /// <summary>Ends the recording <see cref="TryEnterRecording"/> began.</summary>
    public void ExitRecording() => Volatile.Write(ref _recording, 0);

    /// <summary>Records <paramref name="entry"/> if it comes through the innermost open scope and no other recording is in progress.</summary>
    public void AddGuarded(JournalEntry entry)
    {
        if (ScopeContext.Current != Top)
        {
            throw new RewindScopeException(NotInnermost);
        }

        if (!TryEnterRecording())
        {
            throw new RewindScopeException(Simultaneous);
        }

        try
        {
            AddCore(entry);
        }
        finally
        {
            ExitRecording();
        }
    }
}
