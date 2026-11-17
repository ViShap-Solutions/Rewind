using System.Runtime.CompilerServices;
using System.Transactions;

namespace ViShap.Rewind;

/// <summary>
/// The boundary of an atomic unit of state change on one thread. Opened by <see cref="Rewind.Begin"/> or by
/// <see cref="Rewind.Run(Action{RewindScope})"/>; closed by <see cref="Commit"/> or, without a commit, by
/// <see cref="Dispose"/>, which rolls it back.
/// </summary>
/// <remarks>
/// Scopes nest: a scope opened while another is open is its child, and closes before it. A child that commits
/// hands its changes and effects to its parent, which still decides; a child that rolls back undoes only what was
/// done since it was opened. A scope opened by <see cref="Rewind.RunAsync(Func{Task})"/> is used by the flow that opened
/// it, across <see langword="await"/>; any other scope is used only on the thread that opened it.
/// </remarks>
public sealed class RewindScope : IDisposable
{
    private readonly RewindScope? _parent;
    private readonly Journal _journal;
    private readonly int _mark;
    private readonly int _threadId;
    private readonly bool _managed;
    private readonly bool _async;
    private readonly bool _inFlow;
    private List<Action>? _afterCommit;
    private List<Action>? _afterRollback;
    private List<Action<ChangeSet>>? _onCommitted;
    private Transaction? _transaction;
    private TransactionStatus? _outcome;
    private ScopeState _state;

    private RewindScope(RewindScope? parent, bool managed, bool isAsync)
    {
        _parent = parent;
        _journal = parent?._journal ?? NewJournal(isAsync);
        _mark = _journal.Count;
        _threadId = Environment.CurrentManagedThreadId;
        _managed = managed;
        _async = isAsync;
        _inFlow = isAsync || (parent?._inFlow ?? false);
        Depth = parent is null ? 0 : parent.Depth + 1;
        Epoch = ScopeContext.NextEpoch();
        if (parent is null && Transaction.Current is { TransactionInformation.Status: System.Transactions.TransactionStatus.Active } ambient)
        {
            _transaction = ambient;
            ambient.TransactionCompleted += OnTransactionCompleted;
        }
    }

    /// <summary>Gets the nesting depth: 0 for an outermost scope, one more for each enclosing scope.</summary>
    public int Depth { get; }

    /// <summary>Gets whether the scope is still open: neither committed nor rolled back.</summary>
    public bool IsActive => _state == ScopeState.Active;

    /// <summary>Gets the epoch writes compare with; a closed asynchronous scope has one no cell holds, so a write through it is recorded and refused.</summary>
    internal long Epoch { get; private set; }

    internal Journal Journal => _journal;

    internal RewindScope? Parent => _parent;

    /// <summary>Gets whether the scope was opened by <c>Rewind.RunAsync</c> and flows through <see langword="await"/>.</summary>
    internal bool IsAsync => _async;

    /// <summary>Gets the nearest asynchronous scope enclosing this scope, or <see langword="null"/>.</summary>
    internal RewindScope? FlowBase
    {
        get
        {
            for (var scope = _parent; scope is not null; scope = scope._parent)
            {
                if (scope._async)
                {
                    return scope;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Gets whether the scope belongs to an asynchronous flow: it is asynchronous, or nested in an asynchronous scope, and
    /// its journal is guarded. A scope that does not pays nothing for asynchronous scopes.
    /// </summary>
    internal bool InFlow => _inFlow;

    /// <summary>
    /// Gets the scope the asynchronous flow holds while this scope is current: itself if asynchronous, else the nearest
    /// asynchronous scope enclosing it, or <see langword="null"/> when it is not in a flow.
    /// </summary>
    internal RewindScope? Flow => !_inFlow ? null : _async ? this : FlowBase;

    /// <summary>
    /// Registers <paramref name="action"/> to run once the outermost scope has committed. It is discarded if this
    /// scope or any scope enclosing it rolls back.
    /// </summary>
    /// <param name="action">The deferred effect: a message, a notification, anything that must not happen unless the change is kept.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="RewindScopeException">The scope is closed, is not the innermost open scope, or belongs to another thread.</exception>
    public void AfterCommit(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureInnermost();
        var injector = _journal.Injector;
        (_afterCommit ??= []).Add(injector is null ? action : injector.Deferred(action));
        injector?.AfterRegistration(nameof(AfterCommit));
    }

    /// <summary>Registers <paramref name="action"/> to run after this scope, or a scope enclosing it, has rolled back.</summary>
    /// <param name="action">The effect to run once the state has been restored.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="RewindScopeException">The scope is closed, is not the innermost open scope, or belongs to another thread.</exception>
    public void AfterRollback(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureInnermost();
        (_afterRollback ??= []).Add(action);
        _journal.Injector?.AfterRegistration(nameof(AfterRollback));
    }

    /// <summary>
    /// Registers <paramref name="action"/> to receive the <see cref="ChangeSet"/> of the outermost scope once it has
    /// committed: every tracked value it and the scopes nested in it changed. It is discarded if this scope or any scope
    /// enclosing it rolls back. A change set is built only when an action is registered.
    /// </summary>
    /// <param name="action">The observer of the committed changes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="RewindScopeException">The scope is closed, is not the innermost open scope, or belongs to another thread.</exception>
    /// <remarks>
    /// The actions run with no current scope, after the change notifications and before the effects registered with
    /// <see cref="AfterCommit"/>, in registration order. A failure does not undo state and does not stop the others.
    /// </remarks>
    public void OnCommitted(Action<ChangeSet> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureInnermost();
        var injector = _journal.Injector;
        (_onCommitted ??= []).Add(injector is null ? action : injector.Deferred(action));
        injector?.AfterRegistration(nameof(OnCommitted));
    }

    /// <summary>
    /// Runs <paramref name="do"/> now and registers <paramref name="undo"/> as its compensation, run if this scope or a
    /// scope enclosing it rolls back — in reverse order together with the restoration of tracked state.
    /// </summary>
    /// <param name="do">The action with an external result, run immediately.</param>
    /// <param name="undo">The action that cancels that result.</param>
    /// <exception cref="ArgumentNullException"><paramref name="do"/> or <paramref name="undo"/> is <see langword="null"/>.</exception>
    /// <exception cref="RewindScopeException">The scope is closed, is not the innermost open scope, or belongs to another thread.</exception>
    /// <remarks>When <paramref name="do"/> throws, <paramref name="undo"/> is not registered.</remarks>
    public void Effect(Action @do, Action undo)
    {
        ArgumentNullException.ThrowIfNull(@do);
        ArgumentNullException.ThrowIfNull(undo);
        EnsureInnermost();
        @do();
        _journal.Add(new CompensationEntry(undo));
    }

    /// <summary>
    /// Commits the scope. A nested scope hands its changes and effects to its parent; the outermost scope makes
    /// them final and then runs the effects registered with <see cref="AfterCommit"/>.
    /// </summary>
    /// <exception cref="RewindScopeException">
    /// The scope is closed, is not the innermost open scope, belongs to another thread, or was opened by
    /// <see cref="Rewind.Run(Action{RewindScope})"/>, which commits it itself; or the ambient transaction the scope
    /// joined was aborted before the commit, and the scope was rolled back.
    /// </exception>
    /// <exception cref="RewindEffectException">The state was committed and an effect registered with <see cref="AfterCommit"/> threw.</exception>
    /// <exception cref="RewindCompensationException">
    /// The ambient transaction the scope joined was aborted before the commit, and a compensation or a rollback effect
    /// threw while the scope was rolled back.
    /// </exception>
    /// <remarks>
    /// An outermost scope opened in an active ambient <see cref="System.Transactions.Transaction"/> joined it: its commit
    /// keeps the state and waits for the transaction's outcome, and its notifications, observers and effects run when
    /// the transaction commits; if the transaction rolls back, the state is restored.
    /// </remarks>
    public void Commit()
    {
        if (_managed)
        {
            throw new RewindScopeException("A scope opened by Rewind.Run is committed by Rewind.Run; the callback must not commit it.");
        }

        EnsureInnermost();
        CommitCore();
    }

    /// <summary>Rolls the scope back unless it has been committed. Disposing a closed scope does nothing.</summary>
    /// <exception cref="RewindScopeException">The scope is open but is not the innermost open scope, or belongs to another thread.</exception>
    /// <exception cref="RewindCompensationException">A compensation or a rollback effect threw; every other one was still run.</exception>
    public void Dispose()
    {
        if (_state != ScopeState.Active)
        {
            return;
        }

        if (_managed)
        {
            throw new RewindScopeException("A scope opened by Rewind.Run is closed by Rewind.Run; the callback must not dispose it.");
        }

        EnsureInnermost();
        var failures = RollbackCore();
        if (failures is not null)
        {
            throw new RewindCompensationException(null, failures);
        }
    }

    internal static RewindScope Open(bool managed) => Open(managed, isAsync: false);

    /// <summary>Creates the journal of an outermost scope: guarded for an asynchronous one, injecting in an armed flow.</summary>
    private static Journal NewJournal(bool isAsync)
    {
        var injector = FaultInjection.Current;
        if (isAsync)
        {
            return new GuardedJournal(injector);
        }

        return injector is null ? new Journal() : new InjectingJournal(injector);
    }

    /// <summary>Opens a managed asynchronous scope; refused while a synchronous scope is the current one.</summary>
    internal static RewindScope OpenAsync()
    {
        if (ScopeContext.Current is { IsAsync: false })
        {
            throw new RewindScopeException("Rewind.RunAsync cannot run inside a synchronous scope: after an await the block could continue on another thread. Open the asynchronous scope outside it.");
        }

        return Open(managed: true, isAsync: true);
    }

    private static RewindScope Open(bool managed, bool isAsync)
    {
        var parent = ScopeContext.Current;
        if (isAsync || parent is { _inFlow: true })
        {
            return OpenInFlow(parent, managed, isAsync);
        }

        var scope = new RewindScope(parent, managed, isAsync: false);
        ScopeContext.SetCurrent(scope);
        Telemetry.Opened(scope);
        return scope;
    }

    /// <summary>
    /// Opens a scope in an asynchronous flow: refused when the parent is not the innermost open scope of its journal,
    /// recorded as that journal's innermost scope, and held by the flow when it is asynchronous itself.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RewindScope OpenInFlow(RewindScope? parent, bool managed, bool isAsync)
    {
        if (parent is not null && ((GuardedJournal)parent._journal).Top != parent)
        {
            throw new RewindScopeException("A scope was opened inside an asynchronous scope that is not the innermost open scope of its journal: by a parallel branch of the flow, or after the scope closed. An asynchronous scope is used by one flow at a time; give each parallel branch its own outermost scope.");
        }

        var scope = new RewindScope(parent, managed, isAsync);
        ((GuardedJournal)scope._journal).Top = scope;
        ScopeContext.Enter(scope);
        Telemetry.Opened(scope);
        return scope;
    }

    internal Exception? Fail(Exception original)
    {
        Telemetry.Failed(this, original);
        List<Exception>? failures = null;
        UnwindLeakedChildren(ref failures);
        if (_state == ScopeState.Active)
        {
            Append(ref failures, RollbackCore());
        }

        return failures is null ? null : new RewindCompensationException(original, failures);
    }

    internal void CompleteManaged()
    {
        if (Innermost() != this)
        {
            List<Exception>? failures = null;
            UnwindLeakedChildren(ref failures);
            Append(ref failures, RollbackCore());
            var leak = new RewindScopeException("The block left a scope it began open; every change made in the block was rolled back.");
            if (failures is not null)
            {
                throw new RewindCompensationException(leak, failures);
            }

            throw leak;
        }

        CommitCore();
    }

    private void CommitCore()
    {
        _state = ScopeState.Committed;
        Close();
        if (_parent is not null)
        {
            _parent.Adopt(_afterCommit, _afterRollback, _onCommitted);
            Telemetry.NestedCommitted(this);
            return;
        }

        var entries = _journal.Count;
        switch (TransactionStatus())
        {
            case System.Transactions.TransactionStatus.Active:
                TransactionParticipant.Join(_transaction!, new PendingScope(_journal, _onCommitted, _afterCommit, _afterRollback));
                Telemetry.Pending(this, entries);
                return;

            case System.Transactions.TransactionStatus.Aborted:
                _state = ScopeState.RolledBack;
                var rollbackFailures = Completion.Rollback(_journal, _afterRollback);
                Telemetry.Closed(this, Outcome.RolledBack, entries, rollbackFailures?.Count ?? 0);
                var aborted = new RewindScopeException("The ambient transaction was aborted before the scope committed; the scope was rolled back.");
                if (rollbackFailures is not null)
                {
                    throw new RewindCompensationException(aborted, rollbackFailures);
                }

                throw aborted;
        }

        var failures = Completion.Commit(_journal, _onCommitted, _afterCommit);
        Telemetry.Closed(this, Outcome.Committed, entries, failures?.Count ?? 0);
        if (failures is not null)
        {
            throw new RewindEffectException(failures);
        }
    }

    private List<Exception>? RollbackCore()
    {
        _state = ScopeState.RolledBack;
        var entries = _journal.Count - _mark;
        Close();
        List<Exception>? failures = null;
        if (_inFlow)
        {
            var suspended = ScopeContext.Suspend();
            _journal.RollbackTo(_mark, ref failures);
            NotifyRestoredIfOutermost(ref failures);
            Append(ref failures, Completion.RunEffects(_afterRollback));
            ScopeContext.Resume(suspended);
        }
        else
        {
            ScopeContext.SetCurrent(null);
            _journal.RollbackTo(_mark, ref failures);
            NotifyRestoredIfOutermost(ref failures);
            Append(ref failures, Completion.RunEffects(_afterRollback));
            ScopeContext.SetCurrent(_parent);
        }

        if (TransactionStatus() == System.Transactions.TransactionStatus.Active)
        {
            try
            {
                _transaction!.Rollback();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (_parent is null)
        {
            Telemetry.Closed(this, Outcome.RolledBack, entries, failures?.Count ?? 0);
        }
        else
        {
            Telemetry.NestedRolledBack(this, failures?.Count ?? 0);
        }

        return failures;
    }

    /// <summary>
    /// Returns the status of the ambient transaction the scope joined, or <see langword="null"/> when it joined none. Once
    /// the transaction has completed, its outcome is the one it reported, read before it was disposed.
    /// </summary>
    private TransactionStatus? TransactionStatus()
    {
        if (_transaction is null)
        {
            return null;
        }

        if (_outcome is { } outcome)
        {
            return outcome;
        }

        try
        {
            return _transaction.TransactionInformation.Status;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private void OnTransactionCompleted(object? sender, TransactionEventArgs e) =>
        _outcome = e.Transaction?.TransactionInformation.Status;

    private void UnwindLeakedChildren(ref List<Exception>? failures)
    {
        var current = Innermost();
        while (current is not null && current != this && current.Depth > Depth)
        {
            Append(ref failures, current.RollbackCore());
            current = Innermost();
        }
    }

    /// <summary>
    /// Gets the innermost open scope of this scope's journal: the top of a guarded journal, which every flow sees alike, or
    /// the current scope of the thread.
    /// </summary>
    private RewindScope? Innermost() => _inFlow ? ((GuardedJournal)_journal).Top : ScopeContext.Current;

    /// <summary>Makes the parent the innermost scope again.</summary>
    private void Close()
    {
        if (_inFlow)
        {
            CloseInFlow();
            return;
        }

        ScopeContext.SetCurrent(_parent);
    }

    /// <summary>
    /// Closes a scope of an asynchronous flow: the parent becomes the innermost scope of the flow and of the journal, and
    /// the scope gets an epoch no cell holds, so a write a branch makes through it after it closed is recorded and refused.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CloseInFlow()
    {
        ScopeContext.Leave(this);
        ((GuardedJournal)_journal).Top = _parent;
        Epoch = -1;
    }

    private void Adopt(List<Action>? afterCommit, List<Action>? afterRollback, List<Action<ChangeSet>>? onCommitted)
    {
        if (onCommitted is not null)
        {
            (_onCommitted ??= []).AddRange(onCommitted);
        }

        if (afterCommit is not null)
        {
            (_afterCommit ??= []).AddRange(afterCommit);
        }

        if (afterRollback is not null)
        {
            (_afterRollback ??= []).AddRange(afterRollback);
        }
    }

    private void EnsureInnermost()
    {
        if (!_async && _threadId != Environment.CurrentManagedThreadId)
        {
            throw new RewindScopeException("A scope is used only on the thread that opened it.");
        }

        if (_state != ScopeState.Active)
        {
            throw new RewindScopeException("The scope is already closed.");
        }

        if (ScopeContext.Current != this)
        {
            throw new RewindScopeException("The scope is not the innermost open scope; close the scopes opened inside it first.");
        }
    }

    private static void Append(ref List<Exception>? target, List<Exception>? source) => Completion.Append(ref target, source);

    /// <summary>
    /// After the rollback of an outermost scope, notifies what it and its rolled-back children restored. A nested scope
    /// leaves that to its outermost scope, which may still be open.
    /// </summary>
    private void NotifyRestoredIfOutermost(ref List<Exception>? failures)
    {
        if (_parent is null && _journal.HasRestored)
        {
            Completion.NotifyRestored(_journal, ref failures);
        }
    }

    private enum ScopeState
    {
        Active,
        Committed,
        RolledBack,
    }
}
