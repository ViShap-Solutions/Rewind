using BenchmarkDotNet.Attributes;

namespace ViShap.Rewind.Benchmarks.Suites;

/// <summary>
/// W2, W3 — a scope with N writes, then commit or rollback; the writes go to N distinct cells or N times to one cell.
/// Each invocation opens and closes its own scope.
/// </summary>
public class ScopeWriteBenchmarks
{
    private RewindCell<int>[] _cells = [];
    private int[] _plain = [];

    [Params(1, 16, 256)]
    public int Writes { get; set; }

    [Params(Target.Distinct, Target.One)]
    public Target Cells { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _cells = Enumerable.Range(0, Writes).Select(_ => new RewindCell<int>(0)).ToArray();
        _plain = new int[Writes];
    }

    [Benchmark(Baseline = true)]
    public int Plain()
    {
        var plain = _plain;
        var one = Cells == Target.One;
        for (var i = 0; i < plain.Length; i++)
        {
            plain[one ? 0 : i] = i;
        }

        return plain[0];
    }

    [Benchmark]
    public int Commit()
    {
        using var scope = Rewind.Begin();
        Write();
        scope.Commit();
        return _cells[0].Value;
    }

    [Benchmark]
    public int Rollback()
    {
        using (Rewind.Begin())
        {
            Write();
        }

        return _cells[0].Value;
    }

    private void Write()
    {
        var cells = _cells;
        var one = Cells == Target.One;
        for (var i = 0; i < cells.Length; i++)
        {
            cells[one ? 0 : i].Value = i;
        }
    }

    /// <summary>Where the writes go.</summary>
    public enum Target
    {
        /// <summary>Each write to its own cell: every write records an entry.</summary>
        Distinct,

        /// <summary>Every write to the same cell: only the first records.</summary>
        One,
    }
}
