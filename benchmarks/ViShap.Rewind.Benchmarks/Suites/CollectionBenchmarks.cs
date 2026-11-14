using BenchmarkDotNet.Attributes;

namespace ViShap.Rewind.Benchmarks.Suites;

/// <summary>W7 — M mutations of a Rewind collection in a scope, then commit or rollback, against <see cref="List{T}"/>.</summary>
public class CollectionBenchmarks
{
    private readonly List<int> _plainList = [];
    private readonly RewindList<int> _list = [];
    private readonly RewindDictionary<int, int> _dictionary = [];

    [Params(1, 16, 256)]
    public int Mutations { get; set; }

    [Benchmark(Baseline = true)]
    public int PlainListAdd()
    {
        _plainList.Clear();
        for (var i = 0; i < Mutations; i++)
        {
            _plainList.Add(i);
        }

        return _plainList.Count;
    }

    [Benchmark]
    public int ListAddCommit()
    {
        _list.Clear();
        using var scope = Rewind.Begin();
        for (var i = 0; i < Mutations; i++)
        {
            _list.Add(i);
        }

        scope.Commit();
        return _list.Count;
    }

    [Benchmark]
    public int ListAddRollback()
    {
        using (Rewind.Begin())
        {
            for (var i = 0; i < Mutations; i++)
            {
                _list.Add(i);
            }
        }

        return _list.Count;
    }

    [Benchmark]
    public int DictionarySetRollback()
    {
        using (Rewind.Begin())
        {
            for (var i = 0; i < Mutations; i++)
            {
                _dictionary[i] = i;
            }
        }

        return _dictionary.Count;
    }
}

/// <summary>W7 — the rollback of one <c>Clear</c> in a scope, against the size of the cleared list (contract §8.3).</summary>
public class ClearRollbackBenchmarks
{
    private RewindList<int> _list = [];

    [Params(100, 100_000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup() => _list = new RewindList<int>(Enumerable.Range(0, Size));

    [Benchmark]
    public int ClearRollback()
    {
        using (Rewind.Begin())
        {
            _list.Clear();
        }

        return _list.Count;
    }
}
