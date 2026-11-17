using BenchmarkDotNet.Attributes;

namespace ViShap.Rewind.Benchmarks.Market.Suites;

/// <summary>
/// The comparative suites of Track B (T1: undo in-memory changes on one thread when a block fails). Each takes its
/// adapters from <see cref="MarketWorkloads"/>, which is also what the verification checks, so a timed pair is a
/// verified one. Every invocation runs one transaction of the adapter's library and returns a value that depends on
/// it; a failing block's exception is caught in the timed method, the same way for every adapter.
/// </summary>
/// <remarks>The adapter is created and its state prepared in the global setup, outside the timed region, for all alike.</remarks>
public abstract class MarketSuite
{
    private protected MarketAdapter Subject = null!;
    private protected int Value;

    [ParamsSource(nameof(AdapterNames))]
    public string Adapter { get; set; } = string.Empty;

    /// <summary>The adapters of this suite's workload, in roster order.</summary>
    public IEnumerable<string> AdapterNames => MarketWorkloads.Find(GetType().Name).Adapters();

    private protected void Create() => Subject = MarketRoster.Create(Adapter);
}

/// <summary>C1 — N writes to distinct cells in one transaction, commit.</summary>
public class MarketCommitBenchmarks : MarketSuite
{
    [Params(1, 16, 256)]
    public int Writes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Create();
        Subject.Prepare(Writes);
    }

    [Benchmark]
    public int Commit()
    {
        Subject.Commit(Writes, ++Value);
        return Value;
    }
}

/// <summary>C2 — N writes in one transaction, then the block throws; the timed method catches the failure.</summary>
public class MarketRollbackBenchmarks : MarketSuite
{
    [Params(1, 16, 256)]
    public int Writes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Create();
        Subject.Prepare(Writes);
    }

    [Benchmark]
    public int Rollback()
    {
        try
        {
            Subject.Fail(Writes, ++Value);
        }
        catch (MarketFailure)
        {
        }

        return Value;
    }
}

/// <summary>C3 — N reads of distinct cells in one transaction.</summary>
public class MarketReadBenchmarks : MarketSuite
{
    [Params(16, 256)]
    public int Reads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Create();
        Subject.Prepare(Reads);
        Subject.Commit(Reads, 1);
    }

    [Benchmark]
    public int Read() => Subject.Read(Reads);
}

/// <summary>C4 — one write with no transaction open, for the libraries that allow one.</summary>
public class MarketWriteOutsideBenchmarks : MarketSuite
{
    [GlobalSetup]
    public void Setup()
    {
        Create();
        Subject.Prepare(1);
    }

    [Benchmark]
    public int WriteOutside()
    {
        Subject.WriteOutside(++Value);
        return Value;
    }
}

/// <summary>C5 — D nested blocks, one write each; the innermost throws, its parent catches it and every other level commits.</summary>
public class MarketNestedBenchmarks : MarketSuite
{
    [Params(4)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Create();
        Subject.Prepare(Depth);
    }

    [Benchmark]
    public int Nested()
    {
        Subject.Nested(Depth, Depth - 1, ++Value);
        return Value;
    }
}

/// <summary>C6 — in one transaction, clear a list of M items and append M new ones, commit.</summary>
public class MarketListReplaceBenchmarks : MarketSuite
{
    [Params(16, 256)]
    public int Items { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Create();
        Subject.PrepareList(Items);
    }

    [Benchmark]
    public int ListReplace()
    {
        Subject.ListReplace(Items, ++Value);
        return Value;
    }
}

/// <summary>C7 — in one transaction, append M items to a list of M, then the block throws.</summary>
public class MarketListRollbackBenchmarks : MarketSuite
{
    [Params(16, 256)]
    public int Items { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Create();
        Subject.PrepareList(Items);
    }

    [Benchmark]
    public int ListRollback()
    {
        try
        {
            Subject.ListAppendFail(Items);
        }
        catch (MarketFailure)
        {
        }

        return ++Value;
    }
}

/// <summary>C8 — one write and K actions deferred to the commit, which run once it commits.</summary>
public class MarketEffectBenchmarks : MarketSuite
{
    [Params(1, 16)]
    public int Effects { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        Create();
        Subject.Prepare(1);
    }

    [Benchmark]
    public int WithEffects()
    {
        Subject.WithEffects(Effects, ++Value, fail: false);
        return Subject.EffectsRun;
    }
}
