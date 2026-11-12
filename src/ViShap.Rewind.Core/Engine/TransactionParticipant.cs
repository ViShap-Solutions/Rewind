using System.Runtime.CompilerServices;
using System.Transactions;

namespace ViShap.Rewind.Engine;

/// <summary>An outermost scope that committed inside a transaction and waits for its outcome.</summary>
internal sealed record PendingScope(Journal Journal, List<Action<ChangeSet>>? Observers, List<Action>? AfterCommit, List<Action>? AfterRollback);

/// <summary>
/// The volatile resource manager through which the scopes of one transaction follow its outcome. There is one per
/// transaction, so the scopes committed in it are rolled back together, newest first.
/// </summary>
internal sealed class TransactionParticipant : IEnlistmentNotification
{
    private static readonly ConditionalWeakTable<Transaction, TransactionParticipant> s_participants = new();

    private readonly Lock _gate = new();
    private readonly List<PendingScope> _pending = [];

    /// <summary>Adds <paramref name="scope"/> to the participant of <paramref name="transaction"/>, enlisting one when it is the first.</summary>
    public static void Join(Transaction transaction, PendingScope scope)
    {
        var participant = s_participants.GetValue(transaction, static joined =>
        {
            var created = new TransactionParticipant();
            joined.EnlistVolatile(created, EnlistmentOptions.None);
            return created;
        });

        lock (participant._gate)
        {
            participant._pending.Add(scope);
        }
    }

    /// <summary>Returns the participant of <paramref name="transaction"/>, or <see langword="null"/> when no scope joined it.</summary>
    public static TransactionParticipant? Of(Transaction transaction) =>
        s_participants.TryGetValue(transaction, out var participant) ? participant : null;

    /// <inheritdoc />
    public void Prepare(PreparingEnlistment preparingEnlistment) => preparingEnlistment.Prepared();

    /// <inheritdoc />
    public void Commit(Enlistment enlistment)
    {
        var failures = Committed();
        enlistment.Done();
        if (failures is not null)
        {
            throw new RewindEffectException(failures);
        }
    }

    /// <inheritdoc />
    public void Rollback(Enlistment enlistment)
    {
        var failures = RolledBack();
        enlistment.Done();
        if (failures is not null)
        {
            throw new RewindCompensationException(null, failures);
        }
    }

    /// <inheritdoc />
    public void InDoubt(Enlistment enlistment)
    {
        var failures = InDoubt();
        enlistment.Done();
        if (failures is not null)
        {
            throw new RewindEffectException(failures);
        }
    }

    /// <summary>Completes every pending scope as committed, oldest first.</summary>
    internal List<Exception>? Committed()
    {
        List<Exception>? failures = null;
        foreach (var scope in Take())
        {
            var entries = scope.Journal.Count;
            var completion = Completion.Commit(scope.Journal, scope.Observers, scope.AfterCommit);
            Telemetry.TransactionClosed(scope.Journal is GuardedJournal, Outcome.Committed, entries, completion?.Count ?? 0);
            Completion.Append(ref failures, completion);
        }

        return failures;
    }

    /// <summary>Rolls every pending scope back, newest first.</summary>
    internal List<Exception>? RolledBack()
    {
        List<Exception>? failures = null;
        var scopes = Take();
        for (var index = scopes.Length - 1; index >= 0; index--)
        {
            var journal = scopes[index].Journal;
            var entries = journal.Count;
            var completion = Completion.Rollback(journal, scopes[index].AfterRollback);
            Telemetry.TransactionClosed(journal is GuardedJournal, Outcome.RolledBack, entries, completion?.Count ?? 0);
            Completion.Append(ref failures, completion);
        }

        return failures;
    }

    /// <summary>Keeps the state of every pending scope and runs its notifications and observers, but no effect.</summary>
    internal List<Exception>? InDoubt()
    {
        List<Exception>? failures = null;
        foreach (var scope in Take())
        {
            var entries = scope.Journal.Count;
            var completion = Completion.Commit(scope.Journal, scope.Observers, afterCommit: null);
            Telemetry.TransactionClosed(scope.Journal is GuardedJournal, Outcome.InDoubt, entries, completion?.Count ?? 0);
            Completion.Append(ref failures, completion);
        }

        return failures;
    }

    private PendingScope[] Take()
    {
        lock (_gate)
        {
            var scopes = _pending.ToArray();
            _pending.Clear();
            return scopes;
        }
    }
}
