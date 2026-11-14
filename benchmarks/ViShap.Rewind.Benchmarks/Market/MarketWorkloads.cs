namespace ViShap.Rewind.Benchmarks.Market;

/// <summary>
/// One comparative workload of Track B: the suite that times it, the capability an adapter needs to be in it, whether
/// the floor is measured beside it, the sizes, and the check every (adapter, size) pair passes before it is timed.
/// </summary>
/// <remarks>
/// The suites take their adapters from here and the verification checks the same pairs, so a pair cannot be timed
/// without having been verified. <see cref="Check"/> returns <see langword="null"/> when the pair did the job, or what
/// it did instead.
/// </remarks>
internal sealed record MarketWorkload(
    string Id,
    string Suite,
    string Title,
    Capability Capability,
    bool WithFloor,
    string Parameter,
    IReadOnlyList<int> Sizes,
    Func<MarketAdapter, int, string?> Check)
{
    /// <summary>The adapters measured in this workload, in roster order.</summary>
    internal IEnumerable<string> Adapters() =>
        MarketRoster.All()
            .Where(adapter => adapter.IsFloor ? WithFloor : adapter.Declared.Contains(Capability))
            .Select(adapter => adapter.Name);
}

/// <summary>Every comparative workload of Track B, in the order the suites measure them.</summary>
internal static class MarketWorkloads
{
    internal static readonly MarketWorkload Commit = new(
        "C1", "MarketCommitBenchmarks", "N writes, commit", Capability.Rollback, WithFloor: true, "Writes", [1, 16, 256],
        static (adapter, writes) =>
        {
            adapter.Prepare(writes);
            adapter.Commit(writes, 5);
            return Cells(adapter, writes, 5);
        });

    internal static readonly MarketWorkload Rollback = new(
        "C2", "MarketRollbackBenchmarks", "N writes, the block throws", Capability.Rollback, WithFloor: true, "Writes", [1, 16, 256],
        static (adapter, writes) =>
        {
            adapter.Prepare(writes);
            adapter.Commit(writes, 5);

            if (!Throws(() => adapter.Fail(writes, 6)))
            {
                return "the failure did not reach the caller";
            }

            return adapter.IsFloor ? null : Cells(adapter, writes, 5);
        });

    internal static readonly MarketWorkload Read = new(
        "C3", "MarketReadBenchmarks", "N reads in a transaction", Capability.Rollback, WithFloor: true, "Reads", [16, 256],
        static (adapter, reads) =>
        {
            adapter.Prepare(reads);
            adapter.Commit(reads, 2);
            var sum = adapter.Read(reads);
            return sum == 2 * reads ? null : $"read {sum}, expected {2 * reads}";
        });

    internal static readonly MarketWorkload WriteOutside = new(
        "C4", "MarketWriteOutsideBenchmarks", "one write, no transaction", Capability.WriteOutsideTransaction, WithFloor: true, "", [],
        static (adapter, _) =>
        {
            adapter.Prepare(1);
            adapter.WriteOutside(3);
            return Cells(adapter, 1, 3);
        });

    internal static readonly MarketWorkload Nested = new(
        "C5", "MarketNestedBenchmarks", "D nested blocks, the innermost throws and its parent goes on", Capability.NestedRollback, WithFloor: false, "Depth", [4],
        static (adapter, depth) =>
        {
            adapter.Prepare(depth);
            adapter.Nested(depth, depth - 1, 7);

            for (var level = 0; level < depth; level++)
            {
                var expected = level == depth - 1 ? 0 : 7;

                if (adapter.Get(level) != expected)
                {
                    return $"level {level} holds {adapter.Get(level)}, expected {expected}";
                }
            }

            return null;
        });

    internal static readonly MarketWorkload ListReplace = new(
        "C6", "MarketListReplaceBenchmarks", "clear a list and append M items, commit", Capability.CollectionRollback, WithFloor: true, "Items", [16, 256],
        static (adapter, items) =>
        {
            adapter.PrepareList(items);
            adapter.ListReplace(items, 100);
            return adapter.ListCount() == items && adapter.ListItem(0) == 100 && adapter.ListItem(items - 1) == 100 + items - 1
                ? null
                : $"the list holds {adapter.ListCount()} items starting at {adapter.ListItem(0)}";
        });

    internal static readonly MarketWorkload ListRollback = new(
        "C7", "MarketListRollbackBenchmarks", "append M items to a list of M, the block throws", Capability.CollectionRollback, WithFloor: false, "Items", [16, 256],
        static (adapter, items) =>
        {
            adapter.PrepareList(items);

            if (!Throws(() => adapter.ListAppendFail(items)))
            {
                return "the failure did not reach the caller";
            }

            return adapter.ListCount() == items ? null : $"the list holds {adapter.ListCount()} items, expected {items}";
        });

    internal static readonly MarketWorkload Effects = new(
        "C8", "MarketEffectBenchmarks", "one write and K actions deferred to the commit", Capability.CommitEffect, WithFloor: true, "Effects", [1, 16],
        static (adapter, effects) =>
        {
            adapter.Prepare(1);
            var before = adapter.EffectsRun;
            adapter.WithEffects(effects, 3, fail: false);
            return adapter.EffectsRun == before + effects && adapter.Get(0) == 3
                ? null
                : $"{adapter.EffectsRun - before} actions ran, expected {effects}";
        });

    internal static IReadOnlyList<MarketWorkload> All { get; } = [Commit, Rollback, Read, WriteOutside, Nested, ListReplace, ListRollback, Effects];

    internal static MarketWorkload Find(string suite) => All.First(workload => workload.Suite == suite);

    private static string? Cells(MarketAdapter adapter, int count, int expected)
    {
        for (var i = 0; i < count; i++)
        {
            if (adapter.Get(i) != expected)
            {
                return $"cell {i} holds {adapter.Get(i)}, expected {expected}";
            }
        }

        return null;
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (MarketFailure)
        {
            return true;
        }
    }
}
