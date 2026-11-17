namespace ViShap.Rewind.Engine;

/// <summary>
/// The undo log shared by an outermost scope and every scope nested in it. A nested scope is a mark in it: rolling
/// the scope back undoes the entries above its mark, newest first.
/// </summary>
/// <remarks>
/// The journal of an asynchronous outermost scope is a <see cref="GuardedJournal"/>, and that of a synchronous one
/// opened in a flow armed by the Testing package an <see cref="InjectingJournal"/>: both intercept every recording.
/// </remarks>
internal class Journal
{
    private readonly bool _intercepted;
    [ThreadStatic]
    private static NotifyBuffer? t_notifyBuffer;

    [ThreadStatic]
    private static NotifyBuffer? t_current;

    private JournalEntry?[] _entries = new JournalEntry?[16];
    private int _count;
    private List<JournalEntry>? _restored;

    public Journal()
    {
    }

    private protected Journal(bool intercepted)
    {
        _intercepted = intercepted;
    }

    public int Count => _count;

    /// <summary>Gets the fault injector of a journal opened in an armed flow, or <see langword="null"/>.</summary>
    public virtual IFaultInjector? Injector => null;

    /// <summary>Gets whether an entry that raises a notification after the outermost commit was recorded.</summary>
    public bool HasNotifiers { get; private set; }

    /// <summary>Gets whether a rollback kept entries whose members are to be notified when the outermost scope ends.</summary>
    public bool HasRestored => _restored is not null;

    /// <summary>Records that a change recorded in this journal is to be notified, for a recording that decides it itself.</summary>
    public void MarkNotifying() => HasNotifiers = true;

    public void Add(JournalEntry entry)
    {
        if (_intercepted)
        {
            Intercept(entry);
            return;
        }

        AddCore(entry);
    }

    /// <summary>Records <paramref name="entry"/> in a journal that intercepts its recordings.</summary>
    private protected virtual void Intercept(JournalEntry entry) => AddCore(entry);

    private protected void AddCore(JournalEntry entry)
    {
        if (_count == _entries.Length)
        {
            Array.Resize(ref _entries, _count * 2);
        }

        _entries[_count++] = entry;
        if (entry.Notifies)
        {
            HasNotifiers = true;
        }
    }

    /// <summary>
    /// Undoes every entry above <paramref name="mark"/>, newest first; one that throws does not stop the others. An
    /// entry that notifies and whose member differs from its recorded value is kept, so the member is notified once the
    /// outermost scope ends: whatever showed the value being undone — a two-way binding wrote it — shows the restored one.
    /// </summary>
    public void RollbackTo(int mark, ref List<Exception>? failures)
    {
        if (HasNotifiers)
        {
            RollbackKeepingNotifiers(mark, ref failures);
            return;
        }

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

    /// <summary>
    /// <see cref="RollbackTo"/> for a journal that recorded something notifying: before undoing a notifying entry, keeps
    /// it when its member differs from the value it recorded.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void RollbackKeepingNotifiers(int mark, ref List<Exception>? failures)
    {
        for (var index = _count - 1; index >= mark; index--)
        {
            var entry = _entries[index]!;
            _entries[index] = null;
            _count = index;
            try
            {
                if (entry.NotifiesAtEnd && entry.Changed())
                {
                    (_restored ??= []).Add(entry);
                }
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }

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

    /// <summary>Returns the change set of every entry, oldest first; compensations are left out.</summary>
    public ChangeSet ToChangeSet()
    {
        var seen = new HashSet<(object Owner, object? Member)>(IdentityPairComparer.Instance);
        var changes = new List<Change>();
        var entries = new List<JournalEntry>(_count);
        for (var index = 0; index < _count; index++)
        {
            var entry = _entries[index]!;
            if (entry.Owner is not { } owner)
            {
                continue;
            }

            entries.Add(entry);
            if (seen.Add((owner, entry.Member)) && entry.Describe() is { } change)
            {
                changes.Add(change);
            }
        }

        return new ChangeSet(changes, [.. entries]);
    }

    /// <summary>
    /// Returns the entries whose member changed and raises a notification, one per owner and member, or
    /// <see langword="null"/> when there are none. The list comes from a buffer of the thread; hand it back with
    /// <see cref="ReturnNotifications"/>.
    /// </summary>
    public List<JournalEntry>? RentNotifications()
    {
        if (!HasNotifiers && _restored is null)
        {
            return null;
        }

        var buffer = t_notifyBuffer ?? new NotifyBuffer();
        t_notifyBuffer = null;
        for (var index = 0; index < _count; index++)
        {
            var entry = _entries[index]!;
            if (entry.NotifiesAtEnd && buffer.Seen.Add((entry.Owner!, entry.Member)) && entry.Changed())
            {
                buffer.Entries.Add(entry);
            }
        }

        // A member a rolled-back scope changed is notified even though its value is back where it was.
        if (_restored is { } restored)
        {
            for (var index = restored.Count - 1; index >= 0; index--)
            {
                var entry = restored[index];
                if (buffer.Seen.Add((entry.Owner!, entry.Member)))
                {
                    buffer.Entries.Add(entry);
                }
            }

            _restored = null;
        }

        buffer.Seen.Clear();
        if (buffer.Entries.Count == 0)
        {
            t_notifyBuffer = buffer;
            return null;
        }

        t_current = buffer;
        return buffer.Entries;
    }

    /// <summary>Hands back the list <see cref="RentNotifications"/> returned, so the next commit on the thread reuses it.</summary>
    public static void ReturnNotifications(List<JournalEntry> entries)
    {
        if (t_current is { } buffer && ReferenceEquals(buffer.Entries, entries))
        {
            t_current = null;
            entries.Clear();
            t_notifyBuffer = buffer;
        }
    }

    public void Clear()
    {
        Array.Clear(_entries, 0, _count);
        _count = 0;
        HasNotifiers = false;
        _restored = null;
    }

    private sealed class NotifyBuffer
    {
        public HashSet<(object Owner, object? Member)> Seen { get; } = new(IdentityPairComparer.Instance);

        public List<JournalEntry> Entries { get; } = [];
    }
}
