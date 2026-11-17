using Shielded;
using ViShap.Rewind.Benchmarks.Environment;

namespace ViShap.Rewind.Benchmarks.Market.Adapters;

/// <summary>Shielded: <c>Shield.InTransaction</c> over <see cref="Shielded{T}"/> fields and a <see cref="ShieldedSeq{T}"/>.</summary>
internal sealed class ShieldedAdapter : MarketAdapter
{
    private readonly Action _commit;
    private readonly Action _fail;
    private readonly Func<int> _read;
    private readonly Action _listReplace;
    private readonly Action _listFail;
    private readonly Action _withEffects;
    private readonly Action _effect;

    private Shielded<int>[] _cells = [];
    private ShieldedSeq<int> _list = new();
    private int _count;
    private int _value;
    private int _depth;
    private int _failLevel;
    private bool _failing;

    internal ShieldedAdapter()
    {
        _commit = CommitBlock;
        _fail = FailBlock;
        _read = ReadBlock;
        _listReplace = ListReplaceBlock;
        _listFail = ListFailBlock;
        _withEffects = EffectsBlock;
        _effect = () => EffectsRun++;
    }

    internal override string Name => "Shielded";

    internal override string Library => "Shielded";

    internal override string Configuration => "Shield.InTransaction over Shielded<int>, ShieldedSeq<int>, Shield.SideEffect";

    internal override string Version => EnvironmentManifest.Version(typeof(Shield).Assembly);

    internal override string Documentation => "https://github.com/jbakic/Shielded";

    internal override string Rationale =>
        "`Shield.InTransaction(Action)` is the library's one entry point; an exception inside it rolls the " +
        "transaction back and propagates. Measured on one thread, so it pays for an isolation T1 does not need. " +
        "A write outside a transaction is refused by design; a nested `InTransaction` joins the outer transaction, " +
        "so a failure caught inside it keeps the inner writes. `Shield.SideEffect` runs an action after the commit. " +
        "`Shield.Rollback()` is not used: it retries the transaction rather than ending it.";

    internal override IReadOnlySet<Capability> Declared { get; } = new HashSet<Capability>
    {
        Capability.Rollback, Capability.CollectionRollback, Capability.CommitEffect,
    };

    internal override bool Isolates => true;

    internal override void Prepare(int cells) => _cells = [.. Enumerable.Range(0, cells).Select(_ => new Shielded<int>(0))];

    internal override int Get(int index) => _cells[index].Value;

    internal override void Commit(int writes, int value)
    {
        _count = writes;
        _value = value;
        Shield.InTransaction(_commit);
    }

    internal override void Fail(int writes, int value)
    {
        _count = writes;
        _value = value;
        Shield.InTransaction(_fail);
    }

    internal override int Read(int reads)
    {
        _count = reads;
        return Shield.InTransaction(_read);
    }

    internal override void WriteOutside(int value) => _cells[0].Value = value;

    internal override void Nested(int depth, int failLevel, int value)
    {
        _depth = depth;
        _failLevel = failLevel;
        _value = value;
        Shield.InTransaction(() => Level(0));
    }

    internal override void PrepareList(int items) => _list = new ShieldedSeq<int>([.. Enumerable.Range(0, items)]);

    internal override int ListCount() => Shield.InTransaction(() => _list.Count);

    internal override int ListItem(int index) => Shield.InTransaction(() => _list[index]);

    internal override void ListReplace(int items, int first)
    {
        _count = items;
        _value = first;
        Shield.InTransaction(_listReplace);
    }

    internal override void ListAppendFail(int items)
    {
        _count = items;
        Shield.InTransaction(_listFail);
    }

    internal override void WithEffects(int effects, int value, bool fail)
    {
        _count = effects;
        _value = value;
        _failing = fail;
        Shield.InTransaction(_withEffects);
    }

    internal override string? Leak() => Shield.IsInTransaction ? "a transaction is still open after the probe" : null;

    private void CommitBlock()
    {
        var cells = _cells;
        var value = _value;
        for (var i = 0; i < _count; i++)
        {
            cells[i].Value = value;
        }
    }

    private void FailBlock()
    {
        CommitBlock();
        throw MarketFailure.Instance;
    }

    private int ReadBlock()
    {
        var cells = _cells;
        var sum = 0;
        for (var i = 0; i < _count; i++)
        {
            sum += cells[i].Value;
        }

        return sum;
    }

    private void Level(int level)
    {
        _cells[level].Value = _value;

        if (level + 1 < _depth)
        {
            try
            {
                Shield.InTransaction(() => Level(level + 1));
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

    private void ListReplaceBlock()
    {
        var list = _list;
        list.Clear();
        for (var i = 0; i < _count; i++)
        {
            list.Append(_value + i);
        }
    }

    private void ListFailBlock()
    {
        var list = _list;
        for (var i = 0; i < _count; i++)
        {
            list.Append(i);
        }

        throw MarketFailure.Instance;
    }

    private void EffectsBlock()
    {
        _cells[0].Value = _value;
        for (var i = 0; i < _count; i++)
        {
            Shield.SideEffect(_effect);
        }

        if (_failing)
        {
            throw MarketFailure.Instance;
        }
    }
}
