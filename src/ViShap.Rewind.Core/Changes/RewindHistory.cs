namespace ViShap.Rewind;

/// <summary>
/// An undo and redo history of the operations run through it. Each operation is a scope; its committed changes become
/// one step that <see cref="Undo"/> reverts and <see cref="Redo"/> reapplies, each as a scope of its own.
/// </summary>
/// <remarks>
/// A step reverts and reapplies tracked state only — cells, properties of rewindable classes, Rewind collections. The
/// compensations and deferred effects of the operation do not run again. A history assumes that what it recorded is
/// changed only through it between a step and its undo; it is used on one thread at a time.
/// </remarks>
public sealed class RewindHistory
{
    private readonly List<ChangeSet> _undo = [];
    private readonly List<ChangeSet> _redo = [];

    /// <summary>Creates an empty history.</summary>
    public RewindHistory()
    {
    }

    /// <summary>Gets whether there is a step to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Gets whether there is an undone step to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Runs <paramref name="action"/> in a new scope, as <see cref="Rewind.Run(Action)"/> does. Once the outermost scope
    /// commits, its changes become a step to undo, and the steps to redo are dropped. A step that changed nothing is not
    /// recorded; nothing is recorded if a scope enclosing it rolls back.
    /// </summary>
    /// <param name="action">The operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="RewindCompensationException">The operation threw and a compensation or a rollback effect also threw.</exception>
    /// <exception cref="RewindEffectException">The state was committed and an effect or a notification threw.</exception>
    /// <exception cref="RewindScopeException">The operation left a scope it began open.</exception>
    public void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Rewind.Run(scope =>
        {
            scope.OnCommitted(Record);
            action();
        });
    }

    /// <summary>Runs <paramref name="action"/> as <see cref="Run(Action)"/> does, and hands it the scope.</summary>
    /// <param name="action">The operation. It must not commit or dispose the scope it receives.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="RewindCompensationException">The operation threw and a compensation or a rollback effect also threw.</exception>
    /// <exception cref="RewindEffectException">The state was committed and an effect or a notification threw.</exception>
    /// <exception cref="RewindScopeException">The operation left a scope it began open.</exception>
    public void Run(Action<RewindScope> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Rewind.Run(scope =>
        {
            scope.OnCommitted(Record);
            action(scope);
        });
    }

    /// <summary>Reverts the last step, as a scope of its own; it can then be redone.</summary>
    /// <exception cref="InvalidOperationException">There is no step to undo.</exception>
    /// <exception cref="RewindScopeException">A scope is open on the current thread.</exception>
    /// <exception cref="RewindEffectException">The step was reverted and a change notification threw.</exception>
    /// <remarks>If reverting throws, the scope rolls back, the state is as before the call and the step stays to undo.</remarks>
    public void Undo() => Move(_undo, _redo, "There is no step to undo.");

    /// <summary>Reapplies the last undone step, as a scope of its own; it can then be undone again.</summary>
    /// <exception cref="InvalidOperationException">There is no step to redo.</exception>
    /// <exception cref="RewindScopeException">A scope is open on the current thread.</exception>
    /// <exception cref="RewindEffectException">The step was reapplied and a change notification threw.</exception>
    /// <remarks>If reapplying throws, the scope rolls back, the state is as before the call and the step stays to redo.</remarks>
    public void Redo() => Move(_redo, _undo, "There is no step to redo.");

    /// <summary>Forgets every step to undo and to redo.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private void Record(ChangeSet changes)
    {
        if (changes.IsEmpty)
        {
            return;
        }

        _undo.Add(changes);
        _redo.Clear();
    }

    private static void Move(List<ChangeSet> from, List<ChangeSet> to, string nothing)
    {
        if (from.Count == 0)
        {
            throw new InvalidOperationException(nothing);
        }

        if (ScopeContext.Current is not null)
        {
            throw new RewindScopeException("Undo and Redo run as an outermost scope; close the open scope first.");
        }

        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        try
        {
            Rewind.Run(scope =>
            {
                scope.OnCommitted(to.Add);
                step.UndoRecorded();
            });
        }
        catch (RewindEffectException)
        {
            throw;
        }
        catch
        {
            from.Add(step);
            throw;
        }
    }
}
