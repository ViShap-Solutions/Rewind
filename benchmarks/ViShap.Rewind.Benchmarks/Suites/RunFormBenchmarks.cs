using BenchmarkDotNet.Attributes;

namespace ViShap.Rewind.Benchmarks.Suites;

/// <summary>
/// W5 — one write in a committed scope through each form of opening it: a lambda, a lambda with state, a struct
/// operation, and <c>Begin</c>/<c>Commit</c>. The lambdas are static, so no form pays for a closure the caller chose.
/// </summary>
[Config(typeof(TrackAConfig))]
public class RunFormBenchmarks
{
    private static readonly RewindCell<int> s_cell = new(0);

    [Benchmark(Baseline = true)]
    public int BeginCommit()
    {
        using var scope = Rewind.Begin();
        s_cell.Value = 1;
        scope.Commit();
        return s_cell.Value;
    }

    [Benchmark]
    public int RunLambda()
    {
        Rewind.Run(static () => s_cell.Value = 2);
        return s_cell.Value;
    }

    [Benchmark]
    public int RunWithState()
    {
        Rewind.Run(s_cell, static cell => cell.Value = 3);
        return s_cell.Value;
    }

    [Benchmark]
    public int RunStructOperation()
    {
        Rewind.Run(new Write(s_cell, 4));
        return s_cell.Value;
    }

    [Benchmark]
    public int RunWithResult() => Rewind.Run(static () => s_cell.Value = 5);

    private readonly struct Write(RewindCell<int> cell, int value) : IRewindOperation
    {
        public void Execute() => cell.Value = value;
    }
}
