namespace ViShap.Rewind.Engine;

/// <summary>
/// What happens once an outermost scope's outcome is final: after a commit, the notifications, the change set observers
/// and the deferred effects; after a rollback, the restoration of state and the rollback effects. Everything runs with no
/// current scope; a failure is collected and never stops the others.
/// </summary>
internal static class Completion
{
    /// <summary>
    /// Completes the commit of <paramref name="journal"/>: raises the notifications of the changed properties, hands the
    /// change set to <paramref name="observers"/>, then runs <paramref name="afterCommit"/> unless it is
    /// <see langword="null"/>. Clears the journal.
    /// </summary>
    public static List<Exception>? Commit(Journal journal, List<Action<ChangeSet>>? observers, List<Action>? afterCommit)
    {
        var notify = journal.RentNotifications();
        var changes = observers is null ? null : journal.ToChangeSet();
        journal.Clear();

        var suspension = ScopeContext.Suspend();
        List<Exception>? failures = null;
        try
        {
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

            if (observers is not null)
            {
                foreach (var observer in observers)
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

            Append(ref failures, RunEffects(afterCommit));
        }
        finally
        {
            ScopeContext.Resume(suspension);
        }

        return failures;
    }

    /// <summary>
    /// Undoes every entry of <paramref name="journal"/>, newest first, raises the notifications of what the undo restored,
    /// then runs <paramref name="afterRollback"/>.
    /// </summary>
    public static List<Exception>? Rollback(Journal journal, List<Action>? afterRollback)
    {
        var suspension = ScopeContext.Suspend();
        List<Exception>? failures = null;
        try
        {
            journal.RollbackTo(0, ref failures);
            NotifyRestored(journal, ref failures);
            Append(ref failures, RunEffects(afterRollback));
        }
        finally
        {
            ScopeContext.Resume(suspension);
        }

        return failures;
    }

    /// <summary>
    /// Raises, once per owner and member, the notifications of what the rollback of an outermost scope restored. Called
    /// with no current scope, after the state is restored; a handler that throws does not stop the others.
    /// </summary>
    public static void NotifyRestored(Journal journal, ref List<Exception>? failures)
    {
        var notify = journal.RentNotifications();
        if (notify is null)
        {
            return;
        }

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

    /// <summary>Runs every action of <paramref name="effects"/>; one that throws does not stop the others.</summary>
    public static List<Exception>? RunEffects(List<Action>? effects)
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

    /// <summary>Appends <paramref name="source"/> to <paramref name="target"/>, creating it when needed.</summary>
    public static void Append(ref List<Exception>? target, List<Exception>? source)
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
}
