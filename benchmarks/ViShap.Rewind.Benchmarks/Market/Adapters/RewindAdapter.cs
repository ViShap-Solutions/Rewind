using ViShap.Rewind.Benchmarks.Environment;

namespace ViShap.Rewind.Benchmarks.Market.Adapters;

/// <summary>ViShap.Rewind: <c>Rewind.Run</c> over <see cref="RewindCell{T}"/> and <see cref="RewindList{T}"/>.</summary>
internal sealed class RewindAdapter : MarketAdapter
{
    private static readonly Action<(RewindAdapter Adapter, int Level)> s_level = static state => state.Adapter.Level(state.Level);

    private readonly Action _commit;
    private readonly Action _fail;
    private readonly Func<int> _read;
    private readonly Action _listReplace;
    private readonly Action _listFail;
    private readonly Action<RewindScope> _withEffects;
    private readonly Action _effect;

    private RewindCell<int>[] _cells = [];
    private RewindList<int> _list = [];
    private int _count;
    private int _value;
    private int _depth;
    private int _failLevel;
    private bool _failing;

    internal RewindAdapter()
    {
        _commit = CommitBlock;
        _fail = FailBlock;
        _read = ReadBlock;
        _listReplace = ListReplaceBlock;
        _listFail = ListFailBlock;
        _withEffects = EffectsBlock;
        _effect = () => EffectsRun++;
    }

    internal override string Name => "Rewind";

    internal override string Library => "ViShap.Rewind.Core";

    internal override string Configuration => "Rewind.Run over RewindCell<int>, RewindList<int>, scope.AfterCommit";

    internal override string Version => EnvironmentManifest.Version(typeof(Rewind).Assembly);

    internal override string Documentation => "https://github.com/ViShap-Solutions/Rewind/blob/main/docs/scopes.md";

    internal override string Rationale =>
        "`Rewind.Run(Action)` is the entry point the README leads with; a block that throws is rolled back and the " +
        "exception rethrown. Reads cost nothing; a write records the previous value on the first write per scope. " +
        "No isolation between threads: T1 only.";

    internal override IReadOnlySet<Capability> Declared { get; } = new HashSet<Capability>
    {
        Capability.Rollback, Capability.WriteOutsideTransaction, Capability.NestedRollback,
        Capability.CollectionRollback, Capability.CommitEffect,
    };

    internal override bool Isolates => false;

    internal override void Prepare(int cells) => _cells = [.. Enumerable.Range(0, cells).Select(_ => new RewindCell<int>(0))];

    internal override int Get(int index) => _cells[index].Value;

    internal override void Commit(int writes, int value)
    {
        _count = writes;
        _value = value;
        Rewind.Run(_commit);
    }

    internal override void Fail(int writes, int value)
    {
        _count = writes;
        _value = value;
        Rewind.Run(_fail);
    }

    internal override int Read(int reads)
    {
        _count = reads;
        return Rewind.Run(_read);
    }

    internal override void WriteOutside(int value) => _cells[0].Value = value;

    internal override void Nested(int depth, int failLevel, int value)
    {
        _depth = depth;
        _failLevel = failLevel;
        _value = value;
        Rewind.Run((this, 0), s_level);
    }

    internal override void PrepareList(int items) => _list = new RewindList<int>(Enumerable.Range(0, items));

    internal override int ListCount() => _list.Count;

    internal override int ListItem(int index) => _list[index];

    internal override void ListReplace(int items, int first)
    {
        _count = items;
        _value = first;
        Rewind.Run(_listReplace);
    }

    internal override void ListAppendFail(int items)
    {
        _count = items;
        Rewind.Run(_listFail);
    }

    internal override void WithEffects(int effects, int value, bool fail)
    {
        _count = effects;
        _value = value;
        _failing = fail;
        Rewind.Run(_withEffects);
    }

    internal override string? Leak() => Rewind.Current is null ? null : "a scope is still open after the probe";

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
                Rewind.Run((this, level + 1), s_level);
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
            list.Add(_value + i);
        }
    }

    private void ListFailBlock()
    {
        var list = _list;
        for (var i = 0; i < _count; i++)
        {
            list.Add(i);
        }

        throw MarketFailure.Instance;
    }

    private void EffectsBlock(RewindScope scope)
    {
        _cells[0].Value = _value;
        for (var i = 0; i < _count; i++)
        {
            scope.AfterCommit(_effect);
        }

        if (_failing)
        {
            throw MarketFailure.Instance;
        }
    }
}
