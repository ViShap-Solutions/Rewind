namespace ViShap.Rewind.Engine;

/// <summary>The innermost open scope of each thread, and the source of scope epochs.</summary>
internal static class ScopeContext
{
    [ThreadStatic]
    private static RewindScope? t_current;

    private static long s_epoch;

    public static RewindScope? Current => t_current;

    public static void Set(RewindScope? scope) => t_current = scope;

    /// <summary>A number no other scope of the process has had, so a cell can tell whether the current scope already recorded it.</summary>
    public static long NextEpoch() => Interlocked.Increment(ref s_epoch);
}
