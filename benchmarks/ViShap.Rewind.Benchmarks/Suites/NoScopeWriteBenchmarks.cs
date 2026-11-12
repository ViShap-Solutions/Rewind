using BenchmarkDotNet.Attributes;

namespace ViShap.Rewind.Benchmarks.Suites;

/// <summary>W1 — a write with no current scope (INV-01), for a value type and a reference type, against a plain field.</summary>
[Config(typeof(TrackAConfig))]
public class NoScopeWriteBenchmarks
{
    private readonly RewindCell<int> _number = new(0);
    private readonly RewindCell<string> _text = new(string.Empty);
    private readonly string[] _texts = ["a", "b"];
    private int _plainNumber;
    private string _plainText = string.Empty;

    [Benchmark(Baseline = true)]
    public int PlainInt()
    {
        _plainNumber++;
        return _plainNumber;
    }

    [Benchmark]
    public int CellInt()
    {
        _number.Value = _number.Value + 1;
        return _number.Value;
    }

    [Benchmark]
    public string PlainString()
    {
        _plainText = _texts[_plainText.Length & 1];
        return _plainText;
    }

    [Benchmark]
    public string CellString()
    {
        _text.Value = _texts[_text.Value.Length & 1];
        return _text.Value;
    }
}
