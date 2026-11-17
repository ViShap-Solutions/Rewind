using LanguageExt;
using ViShap.Rewind.Benchmarks.Environment;
using static LanguageExt.Prelude;

namespace ViShap.Rewind.Benchmarks.Market.Adapters;

/// <summary>LanguageExt: <c>atomic</c> over <see cref="Ref{A}"/> cells and a <c>Ref&lt;Lst&lt;int&gt;&gt;</c>.</summary>
internal sealed class LanguageExtAdapter : MarketAdapter
{
    private readonly Action _commit;
    private readonly Action _fail;
    private readonly Func<int> _read;
    private readonly Action _listReplace;
    private readonly Action _listFail;

    private Ref<int>[] _cells = [];
    private Ref<Lst<int>> _list = Ref(Lst<int>.Empty);
    private int _count;
    private int _value;
    private int _depth;
    private int _failLevel;

    internal LanguageExtAdapter()
    {
        _commit = CommitBlock;
        _fail = FailBlock;
        _read = ReadBlock;
        _listReplace = ListReplaceBlock;
        _listFail = ListFailBlock;
    }

    internal override string Name => "LanguageExt";

    internal override string Library => "LanguageExt.Core";

    internal override string Configuration => "atomic(Action) with default snapshot isolation over Ref<int>, Ref<Lst<int>>";

    internal override string Version => EnvironmentManifest.Version(typeof(Prelude).Assembly);

    internal override string Documentation => "https://github.com/louthy/language-ext/wiki/Concurrency";

    internal override string Rationale =>
        "`atomic(Action)` with its default `Isolation.Snapshot` is LanguageExt's STM; an exception inside it discards " +
        "the transaction's writes and propagates. A list is an immutable `Lst<int>` held by a `Ref`, the documented " +
        "pairing. A `Ref` can be written only inside a transaction; a nested `atomic` joins the outer transaction, so " +
        "a failure caught inside it keeps the inner writes; there is no action deferred to the commit. 4.4.9 is the " +
        "latest stable release; 5.x is a beta and not measured. Measured on one thread, so it pays for an isolation " +
        "T1 does not need.";

    internal override IReadOnlySet<Capability> Declared { get; } = new System.Collections.Generic.HashSet<Capability>
    {
        Capability.Rollback, Capability.CollectionRollback,
    };

    internal override bool Isolates => true;

    internal override void Prepare(int cells) => _cells = [.. Enumerable.Range(0, cells).Select(_ => Ref(0))];

    internal override int Get(int index) => _cells[index].Value;

    internal override void Commit(int writes, int value)
    {
        _count = writes;
        _value = value;
        atomic(_commit);
    }

    internal override void Fail(int writes, int value)
    {
        _count = writes;
        _value = value;
        atomic(_fail);
    }

    internal override int Read(int reads)
    {
        _count = reads;
        return atomic(_read);
    }

    internal override void WriteOutside(int value) => _cells[0].Value = value;

    internal override void Nested(int depth, int failLevel, int value)
    {
        _depth = depth;
        _failLevel = failLevel;
        _value = value;
        atomic((Action)(() => Level(0)));
    }

    internal override void PrepareList(int items) => _list = Ref(toList(Enumerable.Range(0, items)));

    internal override int ListCount() => _list.Value.Count;

    internal override int ListItem(int index) => _list.Value[index];

    internal override void ListReplace(int items, int first)
    {
        _count = items;
        _value = first;
        atomic(_listReplace);
    }

    internal override void ListAppendFail(int items)
    {
        _count = items;
        atomic(_listFail);
    }

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
                atomic((Action)(() => Level(level + 1)));
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
        var list = Lst<int>.Empty;
        for (var i = 0; i < _count; i++)
        {
            list = list.Add(_value + i);
        }

        _list.Value = list;
    }

    private void ListFailBlock()
    {
        var list = _list.Value;
        for (var i = 0; i < _count; i++)
        {
            list = list.Add(i);
        }

        _list.Value = list;
        throw MarketFailure.Instance;
    }
}
