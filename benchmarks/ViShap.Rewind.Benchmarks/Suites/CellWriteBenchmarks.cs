using BenchmarkDotNet.Attributes;

namespace ViShap.Rewind.Benchmarks.Suites;

/// <summary>The cost of a tracked write: with no scope, in a scope that commits, in a scope that rolls back.</summary>
[MemoryDiagnoser]
public class CellWriteBenchmarks
{
    private RewindCell<int> _cell = null!;
    private int _plain;

    [Params(1, 16)]
    public int Writes { get; set; }

    [GlobalSetup]
    public void Setup() => _cell = new RewindCell<int>(0);

    [Benchmark(Baseline = true)]
    public int PlainField()
    {
        for (var i = 0; i < Writes; i++)
        {
            _plain = i;
        }

        return _plain;
    }

    [Benchmark]
    public int CellWithoutScope()
    {
        for (var i = 0; i < Writes; i++)
        {
            _cell.Value = i;
        }

        return _cell.Value;
    }

    [Benchmark]
    public int CellInCommittedScope()
    {
        using var scope = Rewind.Begin();
        for (var i = 0; i < Writes; i++)
        {
            _cell.Value = i;
        }

        scope.Commit();
        return _cell.Value;
    }

    [Benchmark]
    public int CellInRolledBackScope()
    {
        using (Rewind.Begin())
        {
            for (var i = 0; i < Writes; i++)
            {
                _cell.Value = i;
            }
        }

        return _cell.Value;
    }
}
