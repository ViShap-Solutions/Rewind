using STMSharp.Core;
using STMSharp.Core.Interfaces;
using ViShap.Rewind.Benchmarks.Environment;

namespace ViShap.Rewind.Benchmarks.Market.Adapters;

/// <summary>STMSharp: <c>STMEngine.Atomic</c> over <see cref="STMVariable{T}"/>, awaited synchronously.</summary>
internal sealed class StmSharpAdapter : MarketAdapter
{
    private readonly Action<ITransaction> _commit;
    private readonly Action<ITransaction> _fail;
    private readonly Action<ITransaction> _read;

    private STMVariable<int>[] _cells = [];
    private int _count;
    private int _value;
    private int _depth;
    private int _failLevel;
    private int _sum;

    internal StmSharpAdapter()
    {
        _commit = CommitBlock;
        _fail = FailBlock;
        _read = ReadBlock;
    }

    internal override string Name => "STMSharp";

    internal override string Library => "STMSharp";

    internal override string Configuration => "STMEngine.Atomic(Action<ITransaction>) over STMVariable<int>, default options; reads with StmOptions.ReadOnly";

    internal override string Version => EnvironmentManifest.Version(typeof(STMEngine).Assembly);

    internal override string Documentation => "https://github.com/engineering87/stmsharp";

    internal override string Rationale =>
        "`STMEngine.Atomic` with the default options (three attempts, exponential backoff with jitter) is the README's " +
        "quick start; it returns a task that completes synchronously when nothing conflicts, so it is awaited with " +
        "`GetAwaiter().GetResult()`. Reads use `StmOptions.ReadOnly`, which the README recommends for read-heavy work. " +
        "An exception inside the delegate discards its buffered writes and propagates. A nested `Atomic` is an " +
        "independent transaction that commits on its own; there is no transactional list and no action deferred to " +
        "the commit. Measured on one thread, so it pays for an isolation T1 does not need.";

    internal override IReadOnlySet<Capability> Declared { get; } = new HashSet<Capability>
    {
        Capability.Rollback, Capability.WriteOutsideTransaction,
    };

    internal override bool Isolates => true;

    internal override void Prepare(int cells) => _cells = [.. Enumerable.Range(0, cells).Select(_ => new STMVariable<int>(0))];

    internal override int Get(int index) => _cells[index].Read();

    internal override void Commit(int writes, int value)
    {
        _count = writes;
        _value = value;
        STMEngine.Atomic(_commit).GetAwaiter().GetResult();
    }

    internal override void Fail(int writes, int value)
    {
        _count = writes;
        _value = value;
        STMEngine.Atomic(_fail).GetAwaiter().GetResult();
    }

    internal override int Read(int reads)
    {
        _count = reads;
        STMEngine.Atomic(_read, StmOptions.ReadOnly).GetAwaiter().GetResult();
        return _sum;
    }

    internal override void WriteOutside(int value) => _cells[0].Write(value);

    internal override void Nested(int depth, int failLevel, int value)
    {
        _depth = depth;
        _failLevel = failLevel;
        _value = value;
        STMEngine.Atomic(transaction => Level(transaction, 0)).GetAwaiter().GetResult();
    }

    private void CommitBlock(ITransaction transaction)
    {
        var cells = _cells;
        var value = _value;
        for (var i = 0; i < _count; i++)
        {
            transaction.Write(cells[i], value);
        }
    }

    private void FailBlock(ITransaction transaction)
    {
        CommitBlock(transaction);
        throw MarketFailure.Instance;
    }

    private void ReadBlock(ITransaction transaction)
    {
        var cells = _cells;
        var sum = 0;
        for (var i = 0; i < _count; i++)
        {
            sum += transaction.Read(cells[i]);
        }

        // The engine's only result-returning form takes a task; the sum leaves through a field instead, so no task is
        // allocated for it.
        _sum = sum;
    }

    private void Level(ITransaction transaction, int level)
    {
        transaction.Write(_cells[level], _value);

        if (level + 1 < _depth)
        {
            try
            {
                STMEngine.Atomic(inner => Level(inner, level + 1)).GetAwaiter().GetResult();
            }
            catch (MarketFailure) when (_failLevel == level + 1)
            {
            }
        }

        if (level == _failLevel)
        {
            throw MarketFailure.Instance;
        }
    }
}
