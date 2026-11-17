using BenchmarkDotNet.Attributes;

namespace ViShap.Rewind.Benchmarks.Suites;

/// <summary>
/// W11 — the curve of W2 and W3: one scope with N writes to N distinct cells, then commit or rollback, over a range
/// of N that multiplies. A straight line on the logarithmic chart means the cost grows in proportion to N.
/// </summary>
public class ScopeSizeScalingBenchmarks
{
    private RewindCell<int>[] _cells = [];

    [Params(1, 4, 16, 64, 256, 1024, 4096)]
    public int Writes { get; set; }

    [GlobalSetup]
    public void Setup() => _cells = Enumerable.Range(0, Writes).Select(_ => new RewindCell<int>(0)).ToArray();

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
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i].Value = i + 1;
        }
    }
}

/// <summary>
/// W12 — the curve of W4: scopes nested D deep, one write at each level to its own cell; every level commits, or every
/// level commits and the outermost rolls back.
/// </summary>
public class DepthScalingBenchmarks
{
    private RewindCell<int>[] _cells = [];

    [Params(1, 2, 4, 8, 16, 32, 64)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup() => _cells = Enumerable.Range(0, Depth).Select(_ => new RewindCell<int>(0)).ToArray();

    [Benchmark]
    public int CommitAll()
    {
        Open(0, rollbackOutermost: false);
        return _cells[0].Value;
    }

    [Benchmark]
    public int RollbackOutermost()
    {
        Open(0, rollbackOutermost: true);
        return _cells[0].Value;
    }

    private void Open(int level, bool rollbackOutermost)
    {
        using var scope = Rewind.Begin();
        _cells[level].Value = level + 1;
        if (level + 1 < _cells.Length)
        {
            Open(level + 1, rollbackOutermost);
        }

        if (level != 0 || !rollbackOutermost)
        {
            scope.Commit();
        }
    }
}

/// <summary>
/// W13 — the curve of contract §8.3: the rollback of one <c>Clear</c> of a <see cref="RewindList{T}"/>, and of one
/// <see cref="RewindDictionary{TKey, TValue}"/>, against the size of what was cleared. A flat line is the O(1) the
/// contract promises.
/// </summary>
public class ClearScalingBenchmarks
{
    private RewindList<int> _list = [];
    private RewindDictionary<int, int> _dictionary = [];

    [Params(10, 1_000, 100_000, 1_000_000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _list = new RewindList<int>(Enumerable.Range(0, Size));
        _dictionary = [];

        for (var i = 0; i < Size; i++)
        {
            _dictionary[i] = i;
        }
    }

    [Benchmark]
    public int ListClearRollback()
    {
        using (Rewind.Begin())
        {
            _list.Clear();
        }

        return _list.Count;
    }

    [Benchmark]
    public int DictionaryClearRollback()
    {
        using (Rewind.Begin())
        {
            _dictionary.Clear();
        }

        return _dictionary.Count;
    }
}
