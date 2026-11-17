namespace ViShap.Rewind.Engine;

/// <summary>
/// What the Testing package hands to the engine to fail an operation at a chosen point: told after every recording and
/// every effect registration of an armed scope, it may throw there; it also wraps the deferred effects of such a scope,
/// to see whether they run.
/// </summary>
internal interface IFaultInjector
{
    /// <summary>Called once <paramref name="entry"/> is in the journal; throws to fail the operation at that point.</summary>
    void AfterRecording(JournalEntry entry);

    /// <summary>Called once an effect named <paramref name="registration"/> is registered; throws to fail the operation there.</summary>
    void AfterRegistration(string registration);

    /// <summary>Returns <paramref name="action"/>, registered with <c>AfterCommit</c>, wrapped so its running is observed.</summary>
    Action Deferred(Action action);

    /// <summary>Returns <paramref name="observer"/>, registered with <c>OnCommitted</c>, wrapped so its running is observed.</summary>
    Action<ChangeSet> Deferred(Action<ChangeSet> observer);
}

/// <summary>
/// Arms the calling flow: every outermost scope opened in it, until <see cref="Disarm"/>, gets a journal that reports
/// to the injector. While nothing is armed anywhere, opening an outermost scope reads one static field.
/// </summary>
internal static class FaultInjection
{
    private static int s_armed;

    /// <summary>Gets the injector armed for the calling flow, or <see langword="null"/>.</summary>
    public static IFaultInjector? Current => s_armed == 0 ? null : Flow.Injector.Value;

    /// <summary>Arms the calling flow with <paramref name="injector"/>.</summary>
    public static void Arm(IFaultInjector injector)
    {
        Interlocked.Increment(ref s_armed);
        Flow.Injector.Value = injector;
    }

    /// <summary>Disarms the calling flow.</summary>
    public static void Disarm()
    {
        Flow.Injector.Value = null;
        Interlocked.Decrement(ref s_armed);
    }

    /// <summary>Holds the armed injector apart, so that code which never arms one never initialises it.</summary>
    private static class Flow
    {
        public static readonly AsyncLocal<IFaultInjector?> Injector = new();
    }
}
