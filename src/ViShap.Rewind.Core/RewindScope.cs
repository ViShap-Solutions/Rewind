namespace ViShap.Rewind;

/// <summary>
/// The boundary of an atomic unit of state change on one thread. Opened by <see cref="Rewind.Begin"/> or by
/// <see cref="Rewind.Run(Action{RewindScope})"/>; closed by <see cref="Commit"/> or, without a commit, by
/// <see cref="Dispose"/>, which rolls it back.
/// </summary>
/// <remarks>
/// Scopes nest: a scope opened while another is open is its child, and closes before it. A child that commits
/// hands its changes and effects to its parent, which still decides; a child that rolls back undoes only what was
/// done since it was opened. A scope is used only on the thread that opened it.
/// </remarks>
public sealed class RewindScope : IDisposable
{
    private readonly RewindScope? _parent;
    private readonly Journal _journal;
    private readonly int _mark;
    private readonly int _threadId;
    private readonly bool _managed;
    private List<Action>? _afterCommit;
    private List<Action>? _afterRollback;
    private List<Action<ChangeSet>>? _onCommitted;
    private ScopeState _state;

    private RewindScope(RewindScope? parent, bool managed)
    {
        _parent = parent;
        _journal = parent?._journal ?? new Journal();
        _mark = _journal.Count;
        _threadId = Environment.CurrentManagedThreadId;
        _managed = managed;
        Depth = parent is null ? 0 : parent.Depth + 1;
        Epoch = ScopeContext.NextEpoch();
    }

    /// <summary>Gets the nesting depth: 0 for an outermost scope, one more for each enclosing scope.</summary>
    public int Depth { get; }

    /// <summary>Gets whether the scope is still open: neither committed nor rolled back.</summary>
    public bool IsActive => _state == ScopeState.Active;

    internal long Epoch { get; }

    internal Journal Journal => _journal;

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
        (_afterCommit ??= []).Add(action);
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
        (_onCommitted ??= []).Add(action);
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
    /// <see cref="Rewind.Run(Action{RewindScope})"/>, which commits it itself.
    /// </exception>
    /// <exception cref="RewindEffectException">The state was committed and an effect registered with <see cref="AfterCommit"/> threw.</exception>
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

    internal static RewindScope Open(bool managed)
    {
        var scope = new RewindScope(ScopeContext.Current, managed);
        ScopeContext.Set(scope);
        return scope;
    }

    internal Exception? Fail(Exception original)
    {
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
        if (ScopeContext.Current != this)
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
        if (_parent is not null)
        {
            _parent.Adopt(_afterCommit, _afterRollback, _onCommitted);
            ScopeContext.Set(_parent);
            return;
        }

        var notify = _journal.RentNotifications();
        var changes = _onCommitted is null ? null : _journal.ToChangeSet();
        _journal.Clear();
        ScopeContext.Set(null);

        List<Exception>? failures = null;
        if (notify is not null)
        {
            foreach (var entry in notify)
            {
                try
                {
                    entry.Notify();
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }

            Journal.ReturnNotifications(notify);
        }

        if (_onCommitted is not null)
        {
            foreach (var observer in _onCommitted)
            {
                try
                {
                    observer(changes!);
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }
            }
        }

        Append(ref failures, RunEffects(_afterCommit));
        if (failures is not null)
        {
            throw new RewindEffectException(failures);
        }
    }

    private List<Exception>? RollbackCore()
    {
        _state = ScopeState.RolledBack;
        ScopeContext.Set(null);
        List<Exception>? failures = null;
        _journal.RollbackTo(_mark, ref failures);
        Append(ref failures, RunEffects(_afterRollback));
        ScopeContext.Set(_parent);
        return failures;
    }

    private void UnwindLeakedChildren(ref List<Exception>? failures)
    {
        var current = ScopeContext.Current;
        while (current is not null && current != this && current.Depth > Depth)
        {
            Append(ref failures, current.RollbackCore());
            current = ScopeContext.Current;
        }
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
        if (_threadId != Environment.CurrentManagedThreadId)
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

    private static List<Exception>? RunEffects(List<Action>? effects)
    {
        if (effects is null)
        {
            return null;
        }

        List<Exception>? failures = null;
        foreach (var effect in effects)
        {
            try
            {
                effect();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        return failures;
    }

    private static void Append(ref List<Exception>? target, List<Exception>? source)
    {
        if (source is null)
        {
            return;
        }

        if (target is null)
        {
            target = source;
            return;
        }

        target.AddRange(source);
    }

    private enum ScopeState
    {
        Active,
        Committed,
        RolledBack,
    }
}
