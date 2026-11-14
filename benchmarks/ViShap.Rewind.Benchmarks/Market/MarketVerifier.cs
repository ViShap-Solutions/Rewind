using System.Globalization;
using ViShap.Rewind.Benchmarks.Reporting;

namespace ViShap.Rewind.Benchmarks.Market;

/// <summary>
/// Track B's checks before any timing: every capability of every adapter probed and compared with what the adapter
/// declares, the tiers the probes place each adapter in, and every (adapter, workload, size) pair the suites will time
/// run once and checked. A timing over an unverified pair is not a measurement, so any failure here stops the run.
/// </summary>
/// <remarks>
/// Each probe and each pair runs on a dedicated thread with a fresh adapter, so a library that leaves a transaction or
/// a scope open on its thread cannot hide that from the check after it, nor leak it into the next one.
/// </remarks>
internal static class MarketVerifier
{
    internal const string Supported = "Supported";
    internal const string Unsupported = "Unsupported";
    internal const string Failed = "Failed";

    internal sealed record Probe(string Adapter, Capability Capability, bool Declared, string State, string Detail)
    {
        /// <summary>A probe that disagrees with the declaration is a defect of this harness, not a finding.</summary>
        internal bool Mismatch => Declared != (State == Supported);
    }

    internal sealed record Placement(string Tier, string Adapter, bool Qualified, string Reason);

    internal sealed record Pair(string Adapter, string Workload, string Suite, string Parameters, string State, string Detail);

    internal sealed record Result(IReadOnlyList<Probe> Probes, IReadOnlyList<Placement> Tiers, IReadOnlyList<Pair> Pairs)
    {
        internal int FailedCount => Probes.Count(probe => probe.Mismatch) + Pairs.Count(pair => pair.State != Supported);
    }

    internal static Result VerifyAll()
    {
        var probes = new List<Probe>();

        foreach (var name in MarketRoster.Names)
        {
            foreach (var capability in Enum.GetValues<Capability>())
            {
                var declared = MarketRoster.Create(name).Declared.Contains(capability);
                var (state, detail) = OnThread(name, adapter => ProbeOne(adapter, capability));
                probes.Add(new Probe(name, capability, declared, state, detail));
            }
        }

        var tiers = new List<Placement>();

        foreach (var adapter in MarketRoster.All())
        {
            var rollback = probes.First(probe => probe.Adapter == adapter.Name && probe.Capability == Capability.Rollback);

            tiers.Add(adapter.IsFloor
                ? new Placement("floor", adapter.Name, false, "does none of the job, by construction; the reference every row is read against")
                : new Placement("T1", adapter.Name, rollback.State == Supported,
                    rollback.State == Supported ? "the rollback probe passed" : $"the rollback probe failed: {rollback.Detail}"));

            tiers.Add(new Placement(
                "T2", adapter.Name, false,
                adapter.IsFloor ? "no isolation"
                : adapter.Isolates ? "isolates threads; T2 is not measured by this track"
                : "no isolation between threads"));
        }

        var pairs = new List<Pair>();

        foreach (var workload in MarketWorkloads.All)
        {
            foreach (var name in workload.Adapters())
            {
                foreach (var size in workload.Sizes.Count == 0 ? [0] : workload.Sizes)
                {
                    var parameters = workload.Sizes.Count == 0
                        ? $"Adapter={name}"
                        : $"Adapter={name}; {workload.Parameter}={size.ToString(CultureInfo.InvariantCulture)}";
                    var (state, detail) = OnThread(name, adapter => workload.Check(adapter, size) is { } problem ? (Failed, problem) : (Supported, string.Empty));
                    pairs.Add(new Pair(name, workload.Id, workload.Suite, parameters, state, detail));
                }
            }
        }

        return new Result(probes, tiers, pairs);
    }

    /// <summary>Writes the roster, the probes, the tiers, the pairs, the workloads and what is not measured.</summary>
    internal static void Write(Result result, string directory)
    {
        Csv.Write(Path.Combine(directory, "roster.csv"),
            ["adapter", "library", "version", "configuration", "kind", "isolates", "documentation", "rationale"],
            MarketRoster.All().Select(adapter => (IReadOnlyList<string>)
            [
                adapter.Name, adapter.Library, adapter.Version, adapter.Configuration, adapter.IsFloor ? "floor" : "measured",
                adapter.Isolates ? "true" : "false", adapter.Documentation, adapter.Rationale,
            ]));

        Csv.Write(Path.Combine(directory, "capabilities.csv"),
            ["adapter", "capability", "declared", "probed", "detail"],
            result.Probes.Select(probe => (IReadOnlyList<string>)
                [probe.Adapter, probe.Capability.ToString(), probe.Declared ? "true" : "false", probe.State, probe.Detail]));

        Csv.Write(Path.Combine(directory, "tiers.csv"),
            ["tier", "adapter", "qualified", "reason"],
            result.Tiers.Select(placement => (IReadOnlyList<string>)
                [placement.Tier, placement.Adapter, placement.Qualified ? "true" : "false", placement.Reason]));

        Csv.Write(Path.Combine(directory, "market-verification.csv"),
            ["adapter", "workload", "suite", "parameters", "state", "detail"],
            result.Pairs.Select(pair => (IReadOnlyList<string>)
                [pair.Adapter, pair.Workload, pair.Suite, pair.Parameters, pair.State, pair.Detail]));

        Csv.Write(Path.Combine(directory, "workloads.csv"),
            ["id", "suite", "title", "capability", "floor", "parameter", "sizes"],
            MarketWorkloads.All.Select(workload => (IReadOnlyList<string>)
            [
                workload.Id, workload.Suite, workload.Title, workload.Capability.ToString(), workload.WithFloor ? "true" : "false",
                workload.Parameter, string.Join(" ", workload.Sizes),
            ]));

        Csv.Write(Path.Combine(directory, "unmeasured.csv"),
            ["library", "reason"],
            MarketRoster.Unmeasured.Select(entry => (IReadOnlyList<string>)[entry.Library, entry.Reason]));
    }

    private static (string State, string Detail) ProbeOne(MarketAdapter adapter, Capability capability)
    {
        switch (capability)
        {
            case Capability.Rollback:
                adapter.Prepare(4);
                adapter.Commit(4, 1);

                if (!Throws(() => adapter.Fail(4, 2)))
                {
                    return (Unsupported, "the failure did not reach the caller");
                }

                return Enumerable.Range(0, 4).FirstOrDefault(i => adapter.Get(i) != 1, -1) is var cell and >= 0
                    ? (Unsupported, $"cell {cell} holds {adapter.Get(cell)} after the failure, expected 1")
                    : (Supported, "a failing block left every cell as it was");

            case Capability.WriteOutsideTransaction:
                adapter.Prepare(1);
                adapter.WriteOutside(5);
                return adapter.Get(0) == 5 ? (Supported, "a plain write went through") : (Unsupported, $"the cell holds {adapter.Get(0)}");

            case Capability.NestedRollback:
                adapter.Prepare(3);
                adapter.Nested(3, failLevel: 2, value: 7);

                if (adapter.Get(0) != 7 || adapter.Get(1) != 7 || adapter.Get(2) != 0)
                {
                    return (Unsupported, $"after the innermost failed inside its parent the cells hold {adapter.Get(0)}, {adapter.Get(1)}, {adapter.Get(2)}; expected 7, 7, 0");
                }

                if (!Throws(() => adapter.Nested(3, failLevel: 0, value: 9)))
                {
                    return (Unsupported, "the failure of the outermost block did not reach the caller");
                }

                return adapter.Get(0) != 7 || adapter.Get(1) != 7 || adapter.Get(2) != 0
                    ? (Unsupported, $"after the outermost failed the cells hold {adapter.Get(0)}, {adapter.Get(1)}, {adapter.Get(2)}; expected 7, 7, 0 — a nested block that completed was not undone with its parent")
                    : (Supported, "an inner failure was undone alone, and an outer failure undid the completed inner blocks");

            case Capability.CollectionRollback:
                adapter.PrepareList(3);

                if (!Throws(() => adapter.ListAppendFail(5)))
                {
                    return (Unsupported, "the failure did not reach the caller");
                }

                if (adapter.ListCount() != 3 || adapter.ListItem(2) != 2)
                {
                    return (Unsupported, $"after a failing append the list holds {adapter.ListCount()} items, expected 3");
                }

                adapter.ListReplace(4, 10);
                return adapter.ListCount() == 4 && adapter.ListItem(0) == 10
                    ? (Supported, "a failing append left the list as it was; a replacement committed")
                    : (Unsupported, $"after a replacement the list holds {adapter.ListCount()} items");

            case Capability.CommitEffect:
                adapter.Prepare(1);
                var before = adapter.EffectsRun;
                adapter.WithEffects(3, 1, fail: false);

                if (adapter.EffectsRun != before + 3)
                {
                    return (Unsupported, $"{adapter.EffectsRun - before} of 3 actions ran after the commit");
                }

                if (!Throws(() => adapter.WithEffects(3, 2, fail: true)))
                {
                    return (Unsupported, "the failure did not reach the caller");
                }

                return adapter.EffectsRun != before + 3
                    ? (Unsupported, "an action ran although its block failed")
                    : adapter.Get(0) != 1 ? (Unsupported, "the failing block's write survived") : (Supported, "actions ran after the commit and not after the failure");

            default:
                throw new ArgumentOutOfRangeException(nameof(capability), capability, null);
        }
    }

    /// <summary>
    /// Runs <paramref name="check"/> against a fresh adapter on a thread of its own and turns whatever it throws into a
    /// state: <see cref="NotSupportedException"/> and a library's own refusal are <c>Unsupported</c> for a probe.
    /// </summary>
    private static (string State, string Detail) OnThread(string name, Func<MarketAdapter, (string State, string Detail)> check)
    {
        (string State, string Detail) outcome = (Failed, "the check did not run");

        var thread = new Thread(() =>
        {
            var adapter = MarketRoster.Create(name);

            try
            {
                outcome = check(adapter);
            }
            catch (MarketFailure)
            {
                outcome = (Unsupported, "a failure reached the caller where the block should have absorbed it");
            }
            catch (Exception exception)
            {
                outcome = (Unsupported, $"{exception.GetType().Name}: {exception.Message}");
            }

            if (adapter.Leak() is { } leak)
            {
                outcome = (Failed, leak);
            }
        })
        {
            IsBackground = true,
            Name = $"verify {name}",
        };

        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            return (Failed, "the check did not finish in 30 seconds");
        }

        return outcome;
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
