using System.Collections.Immutable;
using ViShap.Rewind.Benchmarks.Environment;

namespace ViShap.Rewind.Benchmarks.Market.Adapters;

/// <summary>
/// Immutable state swapped whole: <see cref="ImmutableInterlocked.Update{T, TArg}(ref ImmutableArray{T}, Func{ImmutableArray{T}, TArg, ImmutableArray{T}}, TArg)"/>
/// over an <see cref="ImmutableArray{T}"/> and an <see cref="ImmutableList{T}"/>. A transform that throws publishes
/// nothing, which is the rollback.
/// </summary>
internal sealed class ImmutableAdapter : MarketAdapter
{
    private static readonly Func<ImmutableArray<int>, (int Writes, int Value, bool Fail), ImmutableArray<int>> s_write = static (state, argument) =>
    {
        var builder = state.ToBuilder();
        for (var i = 0; i < argument.Writes; i++)
        {
            builder[i] = argument.Value;
        }

        if (argument.Fail)
        {
            throw MarketFailure.Instance;
        }

        return builder.MoveToImmutable();
    };

    private static readonly Func<ImmutableArray<int>, int, ImmutableArray<int>> s_writeOne = static (state, value) => state.SetItem(0, value);

    private static readonly Func<ImmutableList<int>, (int Items, int First), ImmutableList<int>> s_replace = static (_, argument) =>
    {
        var builder = ImmutableList.CreateBuilder<int>();
        for (var i = 0; i < argument.Items; i++)
        {
            builder.Add(argument.First + i);
        }

        return builder.ToImmutable();
    };

    private static readonly Func<ImmutableList<int>, int, ImmutableList<int>> s_appendFail = static (state, items) =>
    {
        var builder = state.ToBuilder();
        for (var i = 0; i < items; i++)
        {
            builder.Add(i);
        }

        throw MarketFailure.Instance;
    };

    private static readonly Func<ImmutableArray<int>, (int Depth, int FailLevel, int Value), ImmutableArray<int>> s_nested =
        static (state, argument) => Level(state, 0, argument.Depth, argument.FailLevel, argument.Value);

    private ImmutableArray<int> _cells = [];
    private ImmutableList<int> _list = [];

    internal override string Name => "ImmutableInterlocked";

    internal override string Library => "System.Collections.Immutable";

    internal override string Configuration => "ImmutableInterlocked.Update over ImmutableArray<int>, ImmutableList<int> builders";

    internal override string Version => EnvironmentManifest.Version(typeof(ImmutableInterlocked).Assembly);

    internal override string Documentation => "https://learn.microsoft.com/dotnet/api/system.collections.immutable.immutableinterlocked.update";

    internal override string Rationale =>
        "The BCL's documented way to change a whole immutable state atomically: `ImmutableInterlocked.Update` with a " +
        "transform over a builder (`ToBuilder`, `MoveToImmutable`) and an argument, so no closure is allocated. A " +
        "transform that throws publishes nothing; a nested transform keeps its parent's value by holding the previous " +
        "reference. No action deferred to the commit: the library has no notion of one.";

    internal override IReadOnlySet<Capability> Declared { get; } = new HashSet<Capability>
    {
        Capability.Rollback, Capability.WriteOutsideTransaction, Capability.NestedRollback, Capability.CollectionRollback,
    };

    internal override bool Isolates => true;

    internal override void Prepare(int cells) => _cells = ImmutableArray.Create(new int[cells]);

    internal override int Get(int index) => _cells[index];

    internal override void Commit(int writes, int value) => ImmutableInterlocked.Update(ref _cells, s_write, (writes, value, false));

    internal override void Fail(int writes, int value) => ImmutableInterlocked.Update(ref _cells, s_write, (writes, value, true));

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

    internal override void WriteOutside(int value) => ImmutableInterlocked.Update(ref _cells, s_writeOne, value);

    internal override void Nested(int depth, int failLevel, int value) =>
        ImmutableInterlocked.Update(ref _cells, s_nested, (depth, failLevel, value));

    internal override void PrepareList(int items) => _list = [.. Enumerable.Range(0, items)];

    internal override int ListCount() => _list.Count;

    internal override int ListItem(int index) => _list[index];

    internal override void ListReplace(int items, int first) => ImmutableInterlocked.Update(ref _list, s_replace, (items, first));

    internal override void ListAppendFail(int items) => ImmutableInterlocked.Update(ref _list, s_appendFail, items);

    private static ImmutableArray<int> Level(ImmutableArray<int> state, int level, int depth, int failLevel, int value)
    {
        state = state.SetItem(level, value);

        if (level + 1 < depth)
        {
            try
            {
                state = Level(state, level + 1, depth, failLevel, value);
            }
            catch (MarketFailure) when (failLevel == level + 1)
            {
            }
        }

        if (level == failLevel)
        {
            throw MarketFailure.Instance;
        }

        return state;
    }
}
