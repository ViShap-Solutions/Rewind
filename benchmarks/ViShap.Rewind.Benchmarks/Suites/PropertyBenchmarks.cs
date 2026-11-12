using BenchmarkDotNet.Attributes;

namespace ViShap.Rewind.Benchmarks.Suites;

/// <summary>
/// W8 — a write to a generated <c>[Rewindable]</c> property, against a <see cref="RewindCell{T}"/> and a plain
/// property; with no scope, and in a committed scope.
/// </summary>
[Config(typeof(TrackAConfig))]
public class PropertyBenchmarks
{
    private readonly Account _account = new();
    private readonly PlainAccount _plain = new();
    private readonly RewindCell<decimal> _cell = new(0m);

    [Benchmark(Baseline = true)]
    public decimal PlainProperty()
    {
        _plain.Balance += 1m;
        return _plain.Balance;
    }

    [Benchmark]
    public decimal GeneratedProperty()
    {
        _account.Balance += 1m;
        return _account.Balance;
    }

    [Benchmark]
    public decimal Cell()
    {
        _cell.Value += 1m;
        return _cell.Value;
    }

    [Benchmark]
    public decimal GeneratedPropertyInScope()
    {
        using var scope = Rewind.Begin();
        _account.Balance += 1m;
        scope.Commit();
        return _account.Balance;
    }

    [Benchmark]
    public decimal CellInScope()
    {
        using var scope = Rewind.Begin();
        _cell.Value += 1m;
        scope.Commit();
        return _cell.Value;
    }
}

/// <summary>A rewindable type with one tracked property.</summary>
[Rewindable]
public sealed partial class Account
{
    /// <summary>Gets or sets the balance.</summary>
    public partial decimal Balance { get; set; }
}

/// <summary>The same shape with a plain property.</summary>
public sealed class PlainAccount
{
    /// <summary>Gets or sets the balance.</summary>
    public decimal Balance { get; set; }
}
