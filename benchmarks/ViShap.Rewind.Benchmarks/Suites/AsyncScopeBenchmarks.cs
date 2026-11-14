using BenchmarkDotNet.Attributes;

namespace ViShap.Rewind.Benchmarks.Suites;

/// <summary>
/// W9 — the cost of an asynchronous scope itself: <c>RunAsync</c> with a block that completes at once and one that
/// yields, committing one write, against the synchronous <c>Run</c>. The synchronous path before and after B9 is the
/// back-to-back measurement of <c>CellWriteBenchmarks</c> recorded with PERF-01.
/// </summary>
public class AsyncScopeBenchmarks
{
    private static readonly RewindCell<int> s_cell = new(0);

    [Benchmark(Baseline = true)]
    public int SyncRun()
    {
        Rewind.Run(static () => s_cell.Value = 1);
        return s_cell.Value;
    }

    [Benchmark]
    public async Task<int> RunAsyncCompleted()
    {
        await Rewind.RunAsync(static () =>
        {
            s_cell.Value = 2;
            return Task.CompletedTask;
        });
        return s_cell.Value;
    }

    [Benchmark]
    public async Task<int> RunAsyncYielding()
    {
        await Rewind.RunAsync(static async () =>
        {
            s_cell.Value = 3;
            await Task.Yield();
            s_cell.Value = 4;
        });
        return s_cell.Value;
    }
}
