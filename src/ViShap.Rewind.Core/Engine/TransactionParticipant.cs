using System.Runtime.CompilerServices;
using System.Transactions;

namespace ViShap.Rewind.Engine;

/// <summary>An outermost scope that committed inside a transaction and waits for its outcome.</summary>
internal sealed record PendingScope(Journal Journal, List<Action<ChangeSet>>? Observers, List<Action>? AfterCommit, List<Action>? AfterRollback);

/// <summary>
/// What the scopes of one transaction follow its outcome through. There is one per transaction, so the scopes committed
/// in it are rolled back together, newest first.
/// </summary>
/// <remarks>
/// It is not a resource manager: it enlists in nothing, so a transaction whose only resource is a single connection
/// still commits that connection in one phase. It subscribes to <see cref="Transaction.TransactionCompleted"/>, raised
/// once every participant has its outcome, and completes the pending scopes there. A failure is thrown from there only
/// on the thread where the first scope joined — the call that completes a local transaction; on any other thread it is
/// left to the diagnostics, since an exception there would reach no caller.
/// </remarks>
internal sealed class TransactionParticipant
{
    private static readonly ConditionalWeakTable<Transaction, TransactionParticipant> s_participants = new();

#if NET9_0_OR_GREATER
    private readonly Lock _gate = new();
#else
    private readonly object _gate = new();
#endif
    private readonly List<PendingScope> _pending = [];
    private readonly int _joinedOn = Environment.CurrentManagedThreadId;

    /// <summary>Adds <paramref name="scope"/> to the participant of <paramref name="transaction"/>, creating one when it is the first.</summary>
    public static void Join(Transaction transaction, PendingScope scope)
    {
        var participant = s_participants.GetValue(transaction, static joined =>
        {
            var created = new TransactionParticipant();
            joined.TransactionCompleted += created.OnTransactionCompleted;
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

    /// <summary>
    /// Completes the pending scopes with the transaction's outcome, and throws their failures when this is the thread where
    /// the first scope joined.
    /// </summary>
    private void OnTransactionCompleted(object? sender, TransactionEventArgs e)
    {
        Exception? failure = (e.Transaction?.TransactionInformation.Status) switch
        {
            TransactionStatus.Committed => Committed() is { } committed ? new RewindEffectException(committed) : null,
            TransactionStatus.InDoubt => InDoubt() is { } inDoubt ? new RewindEffectException(inDoubt) : null,
            _ => RolledBack() is { } rolledBack ? new RewindCompensationException(null, rolledBack) : null,
        };

        if (failure is not null && Environment.CurrentManagedThreadId == _joinedOn)
        {
            throw failure;
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
