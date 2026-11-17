namespace ViShap.Rewind.Benchmarks.Market.Adapters;

/// <summary>
/// The hand-written memento: before a block, copy what it will change; in <c>catch</c>, put it back and rethrow.
/// What a careful developer writes today without a library.
/// </summary>
internal sealed class MementoAdapter : MarketAdapter
{
    private int[] _cells = [];
    private List<int> _list = [];
    private readonly Action _effect;

    internal MementoAdapter() => _effect = () => EffectsRun++;

    internal override string Name => "Memento";

    internal override string Library => "hand-written (no library)";

    internal override string Configuration => "copy before the block, restore in catch; List<Action> run after it";

    internal override string Version => System.Environment.Version.ToString();

    internal override string Documentation => "https://en.wikipedia.org/wiki/Memento_pattern";

    internal override string Rationale =>
        "The values a block will change are copied into a new array before it and written back in `catch`; a list " +
        "remembers its count before appends and its content before a clear; deferred actions wait in a `List<Action>` " +
        "and run once the block returned. A nested block snapshots everything it and its children touch, which a " +
        "correct hand-written nesting needs. Each block is written for its own state: nothing here is reusable.";

    internal override IReadOnlySet<Capability> Declared { get; } = new HashSet<Capability>
    {
        Capability.Rollback, Capability.WriteOutsideTransaction, Capability.NestedRollback,
        Capability.CollectionRollback, Capability.CommitEffect,
    };

    internal override bool Isolates => false;

    internal override void Prepare(int cells) => _cells = new int[cells];

    internal override int Get(int index) => _cells[index];

    internal override void Commit(int writes, int value) => Write(writes, value, fail: false);

    internal override void Fail(int writes, int value) => Write(writes, value, fail: true);

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
        var saved = list.ToArray();

        try
        {
            list.Clear();
            for (var i = 0; i < items; i++)
            {
                list.Add(first + i);
            }
        }
        catch
        {
            list.Clear();
            list.AddRange(saved);
            throw;
        }
    }

    internal override void ListAppendFail(int items)
    {
        var list = _list;
        var count = list.Count;

        try
        {
            for (var i = 0; i < items; i++)
            {
                list.Add(i);
            }

            throw MarketFailure.Instance;
        }
        catch
        {
            list.RemoveRange(count, list.Count - count);
            throw;
        }
    }

    internal override void WithEffects(int effects, int value, bool fail)
    {
        var cells = _cells;
        var saved = cells[0];
        var pending = new List<Action>(effects);

        try
        {
            cells[0] = value;
            for (var i = 0; i < effects; i++)
            {
                pending.Add(_effect);
            }

            if (fail)
            {
                throw MarketFailure.Instance;
            }
        }
        catch
        {
            cells[0] = saved;
            throw;
        }

        foreach (var effect in pending)
        {
            effect();
        }
    }

    private void Write(int writes, int value, bool fail)
    {
        var cells = _cells;
        var saved = new int[writes];
        Array.Copy(cells, saved, writes);

        try
        {
            for (var i = 0; i < writes; i++)
            {
                cells[i] = value;
            }

            if (fail)
            {
                throw MarketFailure.Instance;
            }
        }
        catch
        {
            Array.Copy(saved, cells, writes);
            throw;
        }
    }

    private void Level(int level, int depth, int failLevel, int value)
    {
        var cells = _cells;
        var saved = cells[level..depth];

        try
        {
            cells[level] = value;

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
        catch
        {
            Array.Copy(saved, 0, cells, level, saved.Length);
            throw;
        }
    }
}
