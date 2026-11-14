using BenchmarkDotNet.Attributes;

namespace ViShap.Rewind.Benchmarks.Suites;

/// <summary>
/// W4 — scopes nested D deep, one write at each level to its own cell; every level commits, or the innermost rolls
/// back and every level above commits, or every level commits and the outermost rolls back.
/// </summary>
public class NestingBenchmarks
{
    private RewindCell<int>[] _cells = [];

    [Params(1, 4, 16)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup() => _cells = Enumerable.Range(0, Depth).Select(_ => new RewindCell<int>(0)).ToArray();

    [Benchmark(Baseline = true)]
    public int CommitAll()
    {
        Open(0, rollbackLevel: -1);
        return _cells[0].Value;
    }

    [Benchmark]
    public int RollbackInnermost()
    {
        Open(0, rollbackLevel: Depth - 1);
        return _cells[0].Value;
    }

    [Benchmark]
    public int RollbackOutermost()
    {
        Open(0, rollbackLevel: 0);
        return _cells[0].Value;
    }

    private void Open(int level, int rollbackLevel)
    {
        using var scope = Rewind.Begin();
        _cells[level].Value = level + 1;
        if (level + 1 < _cells.Length)
        {
            Open(level + 1, rollbackLevel);
        }

        if (level != rollbackLevel)
        {
            scope.Commit();
        }
    }
}
