namespace ViShap.Rewind.Engine;

/// <summary>
/// The innermost open scope of the running code, and the source of scope epochs. Writes read only the thread-static
/// <see cref="Current"/>. An asynchronous scope is also held in an <see cref="AsyncLocal{T}"/>, which flows through
/// <see langword="await"/>; whenever the execution context of a thread changes, the thread's current scope is set from
/// it, so a flow resumed on another thread finds its scope there.
/// </summary>
internal static class ScopeContext
{
    [ThreadStatic]
    private static RewindScope? t_current;

    [ThreadStatic]
    private static Stack<RewindScope>? t_displaced;

    private static long s_epoch;

    public static RewindScope? Current => t_current;

    /// <summary>A number no other scope of the process has had, so a cell can tell whether the current scope already recorded it.</summary>
    public static long NextEpoch() => Interlocked.Increment(ref s_epoch);

    /// <summary>
    /// Makes <paramref name="scope"/> the innermost scope: of the calling flow for an asynchronous scope, of the thread for
    /// a synchronous one.
    /// </summary>
    public static void Enter(RewindScope scope)
    {
        if (scope.IsAsync)
        {
            Flow.Scope.Value = scope;
        }

        t_current = scope;
    }

    /// <summary>Makes the parent of <paramref name="scope"/>, which has closed, the innermost scope again.</summary>
    public static void Leave(RewindScope scope)
    {
        var parent = scope.Parent;
        if (scope.IsAsync)
        {
            Flow.Scope.Value = parent;
        }

        t_current = parent;
    }

    /// <summary>
    /// Leaves the running code with no current scope until <see cref="Resume"/>, for a rollback or effects, and returns
    /// what to resume. The flow's scope is cleared too when the current scope belongs to an asynchronous one.
    /// </summary>
    public static RewindScope? Suspend()
    {
        var current = t_current;
        t_current = null;
        if (current?.Flow is not null)
        {
            Flow.Scope.Value = null;
        }

        return current;
    }

    /// <summary>Makes <paramref name="current"/>, returned by <see cref="Suspend"/>, the current scope again.</summary>
    public static void Resume(RewindScope? current)
    {
        if (current?.Flow is { } flow)
        {
            Flow.Scope.Value = flow;
        }

        t_current = current;
    }

    /// <summary>
    /// Sets the thread's current scope when another execution context takes over the thread. A synchronous scope the
    /// incoming context displaces is kept, and becomes current again when the flow it was opened in comes back to the
    /// thread; otherwise the incoming flow's scope is current.
    /// </summary>
    private static void OnFlowChanged(AsyncLocalValueChangedArgs<RewindScope?> args)
    {
        if (!args.ThreadContextChanged)
        {
            return;
        }

        if (t_current is { IsAsync: false, IsActive: true } displacing)
        {
            (t_displaced ??= new Stack<RewindScope>()).Push(displacing);
        }

        if (t_displaced is { } displaced)
        {
            while (displaced.TryPeek(out var stale) && !stale.IsActive)
            {
                displaced.Pop();
            }

            if (displaced.TryPeek(out var sync) && sync.FlowBase == args.CurrentValue)
            {
                displaced.Pop();
                t_current = sync;
                return;
            }
        }

        t_current = args.CurrentValue;
    }

    /// <summary>
    /// Holds the flow's scope apart from <see cref="ScopeContext"/>, so that code which never opens an asynchronous scope
    /// never initialises it.
    /// </summary>
    private static class Flow
    {
        public static readonly AsyncLocal<RewindScope?> Scope = new(OnFlowChanged);
    }
}
