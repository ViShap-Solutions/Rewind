namespace ViShap.Rewind.Benchmarks.Market.Adapters;

/// <summary>
/// The floor: plain fields, a plain <see cref="List{T}"/>, effects run on the spot. It does none of the job — nothing
/// is undone — so it belongs to no tier; it is the cost of doing nothing, and every row is read against it.
/// </summary>
internal sealed class FloorAdapter : MarketAdapter
{
    private int[] _cells = [];
    private List<int> _list = [];

    internal override string Name => "Floor";

    internal override string Library => "plain fields (no library)";

    internal override string Configuration => "int[], List<int>, actions invoked at once";

    internal override string Version => System.Environment.Version.ToString();

    internal override string Documentation => "—";

    internal override string Rationale =>
        "The cost of doing nothing: the same writes, reads and appends with no rollback, so a cell near it is a " +
        "harness artifact and a cell above it is the price of the job. It fails the rollback probe by construction " +
        "and is in no tier.";

    internal override IReadOnlySet<Capability> Declared { get; } = new HashSet<Capability> { Capability.WriteOutsideTransaction };

    internal override bool Isolates => false;

    internal override bool IsFloor => true;

    internal override void Prepare(int cells) => _cells = new int[cells];

    internal override int Get(int index) => _cells[index];

    internal override void Commit(int writes, int value)
    {
        var cells = _cells;
        for (var i = 0; i < writes; i++)
        {
            cells[i] = value;
        }
    }

    internal override void Fail(int writes, int value)
    {
        Commit(writes, value);
        throw MarketFailure.Instance;
    }

    internal override int Read(int reads)
    {
        var cells = _cells;
        var sum = 0;
        for (var i = 0; i < reads; i++)
        {
            sum += cells[i];
        }

        return sum;
    }

    internal override void WriteOutside(int value) => _cells[0] = value;

    internal override void Nested(int depth, int failLevel, int value) => Level(0, depth, failLevel, value);

    internal override void PrepareList(int items) => _list = [.. Enumerable.Range(0, items)];

    internal override int ListCount() => _list.Count;

    internal override int ListItem(int index) => _list[index];

    internal override void ListReplace(int items, int first)
    {
        var list = _list;
        list.Clear();
        for (var i = 0; i < items; i++)
        {
            list.Add(first + i);
        }
    }

    internal override void ListAppendFail(int items)
    {
        for (var i = 0; i < items; i++)
        {
            _list.Add(i);
        }

        throw MarketFailure.Instance;
    }

    internal override void WithEffects(int effects, int value, bool fail)
    {
        _cells[0] = value;
        for (var i = 0; i < effects; i++)
        {
            EffectsRun++;
        }

        if (fail)
        {
            throw MarketFailure.Instance;
        }
    }

    private void Level(int level, int depth, int failLevel, int value)
    {
        _cells[level] = value;

        if (level + 1 < depth)
        {
            try
            {
                Level(level + 1, depth, failLevel, value);
            }
            catch (MarketFailure) when (failLevel == level + 1)
            {
            }
        }

        if (level == failLevel)
        {
            throw MarketFailure.Instance;
        }
    }
}
