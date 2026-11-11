namespace ViShap.Rewind.Tests.Fixtures;

/// <summary>Measures the bytes the current thread allocates while an action runs.</summary>
public static class Allocations
{
    private const int WarmUpRuns = 16;

    /// <summary>Runs <paramref name="action"/> until it is compiled at its final tier, then measures one run.</summary>
    public static long Measure(Action action)
    {
        for (var i = 0; i < WarmUpRuns; i++)
        {
            action();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
