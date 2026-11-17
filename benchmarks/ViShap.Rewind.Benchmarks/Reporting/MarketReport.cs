using System.Globalization;
using ViShap.Rewind.Benchmarks.Market;

namespace ViShap.Rewind.Benchmarks.Reporting;

/// <summary>
/// The sections of a Track B report, drawn from the run's own files. It opens with the tiers, the roster and what was
/// not measured before any number; prints every adapter's rationale and probed capabilities; and shows a timing only
/// in a workload table that holds the adapters whose capability the workload needs.
/// </summary>
/// <remarks>
/// Rows keep the roster's order and are never sorted by a number. A ratio is to Rewind's cell of the same size, beside
/// the absolute it comes from; <c>≈</c> marks two cells whose 99.9 % intervals overlap — no measurable difference,
/// not a ranking.
/// </remarks>
internal static class MarketReport
{
    private static readonly string[] CapabilityColumns =
        [nameof(Capability.Rollback), nameof(Capability.WriteOutsideTransaction), nameof(Capability.NestedRollback), nameof(Capability.CollectionRollback), nameof(Capability.CommitEffect)];

    internal static void Write(Document document, string directory, IReadOnlyList<ResultRow> results)
    {
        var roster = Csv.Read(Path.Combine(directory, "roster.csv"));
        var capabilities = Csv.Read(Path.Combine(directory, "capabilities.csv"));
        var tiers = Csv.Read(Path.Combine(directory, "tiers.csv"));
        var pairs = Csv.Read(Path.Combine(directory, "market-verification.csv"));
        var workloads = Csv.Read(Path.Combine(directory, "workloads.csv"));
        var unmeasured = Csv.Read(Path.Combine(directory, "unmeasured.csv"));
        var order = roster.Select(row => row["adapter"]).ToList();
        var cells = results.Where(row => row.Parameter("Adapter") is not null).ToList();

        Caveats(document);
        Tiers(document, tiers, order);
        Roster(document, roster, unmeasured);
        ReportWriter.Environment(document, directory);
        Capabilities(document, capabilities, order);
        Verification(document, pairs);
        Rationale(document, roster);

        var charts = new List<Chart>();
        var chartsDirectory = Path.Combine(directory, "charts");
        Directory.CreateDirectory(chartsDirectory);

        foreach (var workload in workloads)
        {
            var rows = cells.Where(row => row.SuiteName == workload["suite"]).ToList();

            if (rows.Count == 0)
            {
                continue;
            }

            Workload(document, workload, rows, order, capabilities);

            if (Chart(chartsDirectory, workload, rows, order) is { } chart)
            {
                charts.Add(chart);
            }
        }

        if (Allocation(chartsDirectory, workloads, cells, order) is { } allocation)
        {
            charts.Add(allocation);
        }

        if (charts.Count > 0)
        {
            document.Heading(2, "Charts");
            document.Paragraph("Generated from the files above and nothing else. Whiskers are the 99.9 % interval; the hatched bar is the floor, which does none of the job.");

            foreach (var chart in charts)
            {
                document.Image($"charts/{chart.File}", $"{chart.Title} — {chart.Note}");
            }
        }

        ReportWriter.Caution(document, results);
    }

    private static void Caveats(Document document)
    {
        document.Heading(2, "What this record is, and what it is not");

        document.Bullets(
        [
            "**T1 only: undo in-memory changes on one thread when a block fails.** Libraries that also isolate threads are measured on one thread here and pay for an isolation T1 does not need; that is said in their rationale, and T2 is not measured.",
            "**There is no winner.** Rows keep the roster's order and are never sorted by a number. A ratio is to Rewind's cell of the same size and stands beside the absolute; `≈` marks two cells whose 99.9 % intervals overlap, which is no measurable difference.",
            "**A workload holds only the adapters that can do it.** The probes of this run decide; an adapter left out of a table is named under it with the probe's reason.",
            "**Every alternative runs in its own documented best mode**, as its rationale says. An adapter that could be configured better is a defect of this harness.",
            "**A failing block fails the same way for everyone:** one pre-built exception thrown inside the block and caught by the timed method.",
            "**The floor is plain code** that does none of the job; it is the cost of doing nothing, hatched in the charts and in no tier.",
            "**Every timing belongs to the machine below**; allocation per operation, the probes and the tiers do not.",
        ]);
    }

    private static void Tiers(Document document, IReadOnlyList<IReadOnlyDictionary<string, string>> tiers, IReadOnlyList<string> order)
    {
        document.Heading(2, "Tiers");
        document.Paragraph("Placed by the probes of this run (`tiers.csv`, `capabilities.csv`), never by hand.");

        var rows = new List<IReadOnlyList<string>>();

        foreach (var tier in new[] { "T1", "floor", "T2" })
        {
            var placed = tiers.Where(row => row["tier"] == tier).OrderBy(row => order.ToList().IndexOf(row["adapter"])).ToList();

            if (placed.Count == 0)
            {
                continue;
            }

            var members = placed.Where(row => row["qualified"] == "true").Select(row => row["adapter"]).ToList();
            var outside = placed.Where(row => row["qualified"] != "true").Select(row => $"{row["adapter"]}: {row["reason"]}").ToList();

            rows.Add(tier switch
            {
                "T1" => ["T1 — undo on one thread when a block fails", members.Count == 0 ? "none" : string.Join(", ", members), outside.Count == 0 ? "—" : string.Join("; ", outside)],
                "floor" => ["Floor — the cost of doing nothing", string.Join(", ", placed.Select(row => row["adapter"])), "in no tier: " + placed[0]["reason"]],
                _ => ["T2 — isolation between threads", "not measured by this track", string.Join("; ", outside)],
            });
        }

        document.Table(["Tier", "Members", "Outside it, and why"], rows);
    }

    private static void Roster(Document document, IReadOnlyList<IReadOnlyDictionary<string, string>> roster, IReadOnlyList<IReadOnlyDictionary<string, string>> unmeasured)
    {
        document.Heading(2, "Roster");
        document.Table(
            ["Adapter", "Library", "Version", "Configuration", "Isolates threads", "Documentation"],
            [.. roster.Select(row => (IReadOnlyList<string>)
            [
                row["kind"] == "floor" ? $"{row["adapter"]} (floor)" : row["adapter"], row["library"], $"`{row["version"]}`", row["configuration"],
                row["isolates"] == "true" ? "yes" : "no", row["documentation"],
            ])]);

        if (unmeasured.Count > 0)
        {
            document.Heading(3, "Not measured");
            document.Table(["What", "Why"], [.. unmeasured.Select(row => (IReadOnlyList<string>)[row["library"], row["reason"]])]);
        }
    }

    private static void Capabilities(Document document, IReadOnlyList<IReadOnlyDictionary<string, string>> capabilities, IReadOnlyList<string> order)
    {
        if (capabilities.Count == 0)
        {
            return;
        }

        document.Heading(2, "Capabilities, as probed");
        document.Paragraph(
            "Each capability of each adapter was exercised on a thread of its own before any timing, and the result compared with what the adapter declares; a disagreement stops the run. `yes` is a probe that passed, `no` one that did not.");

        document.Table(
            ["Adapter", .. CapabilityColumns],
            [.. order.Select(adapter => (IReadOnlyList<string>)
            [
                adapter,
                .. CapabilityColumns.Select(capability => capabilities.FirstOrDefault(row => row["adapter"] == adapter && row["capability"] == capability) is { } probe
                    ? probe["probed"] == MarketVerifier.Supported ? "yes" : "no"
                    : "—"),
            ])]);

        var refused = capabilities.Where(row => row["probed"] != MarketVerifier.Supported).ToList();

        if (refused.Count > 0)
        {
            document.Heading(3, "What a probe saw when it did not pass");
            document.Table(["Adapter", "Capability", "Observed"], [.. refused.Select(row => (IReadOnlyList<string>)[row["adapter"], row["capability"], row["detail"]])]);
        }
    }

    private static void Verification(Document document, IReadOnlyList<IReadOnlyDictionary<string, string>> pairs)
    {
        if (pairs.Count == 0)
        {
            return;
        }

        document.Heading(2, "Verification before timing");
        document.Paragraph($"{pairs.Count} (adapter, workload, size) pairs were run once and checked — the values written, read, restored, the list content, the actions run — before any suite was allowed to time them.");
        document.Table(["State", "Pairs"], [.. pairs.GroupBy(row => row["state"]).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (IReadOnlyList<string>)[group.Key, group.Count().ToString(CultureInfo.InvariantCulture)])]);
    }

    private static void Rationale(Document document, IReadOnlyList<IReadOnlyDictionary<string, string>> roster)
    {
        document.Heading(2, "Why each adapter is configured as it is");
        document.Bullets(roster.Select(row => $"**{row["adapter"]}** — {row["rationale"]}"));
    }

    private static void Workload(
        Document document,
        IReadOnlyDictionary<string, string> workload,
        IReadOnlyList<ResultRow> rows,
        IReadOnlyList<string> order,
        IReadOnlyList<IReadOnlyDictionary<string, string>> capabilities)
    {
        var parameter = workload["parameter"];

        document.Heading(2, $"{workload["id"]} — {workload["title"]}");

        var needs = workload["capability"];
        var excluded = order
            .Where(adapter => rows.All(row => row.Parameter("Adapter") != adapter))
            .Select(adapter => capabilities.FirstOrDefault(row => row["adapter"] == adapter && row["capability"] == needs) is { } probe && probe["probed"] != MarketVerifier.Supported
                ? $"{adapter} ({probe["detail"]})"
                : adapter == "Floor" ? "Floor (without a rollback it would not do the same work here)" : adapter)
            .ToList();

        document.Paragraph(
            $"Needs `{needs}`." + (excluded.Count > 0 ? $" Not in this table: {string.Join("; ", excluded)}." : " Every adapter of the roster is in this table."));

        var sizes = parameter.Length == 0 ? [string.Empty] : rows.Select(row => row.Parameter(parameter) ?? string.Empty).Distinct().OrderBy(size => double.TryParse(size, CultureInfo.InvariantCulture, out var value) ? value : 0).ToList();
        var table = new List<IReadOnlyList<string>>();

        foreach (var size in sizes)
        {
            var group = rows.Where(row => parameter.Length == 0 || row.Parameter(parameter) == size).OrderBy(row => order.ToList().IndexOf(row.Parameter("Adapter")!)).ToList();
            var rewind = group.FirstOrDefault(row => row.Parameter("Adapter") == MarketRoster.Rewind);

            foreach (var row in group)
            {
                var adapter = row.Parameter("Adapter")!;
                var ratio = rewind is { IsSupported: true, MeanNs: > 0 } && row.IsSupported
                    ? adapter == MarketRoster.Rewind
                        ? "1.00×"
                        : (Confidence.Overlap(row.MeanNs, row.MarginNs, rewind.MeanNs, rewind.MarginNs) ? "≈ " : string.Empty) + Format.Ratio(row.MeanNs / rewind.MeanNs)
                    : "—";

                table.Add(
                [
                    adapter == "Floor" ? "Floor (no rollback)" : adapter,
                    .. parameter.Length == 0 ? Array.Empty<string>() : [size],
                    Format.Interval(row),
                    ratio,
                    Format.Bytes(row.AllocatedBytes),
                    Format.Decimal(row.Gen0Per1000),
                ]);
            }
        }

        document.Table(
            ["Adapter", .. parameter.Length == 0 ? Array.Empty<string>() : [parameter], "Mean ± margin", "÷ Rewind", "Allocated", "Gen0/1k ops"],
            table);
    }

    private static Chart? Chart(string directory, IReadOnlyDictionary<string, string> workload, IReadOnlyList<ResultRow> rows, IReadOnlyList<string> order)
    {
        var parameter = workload["parameter"];
        var chart = new BarChart($"{workload["id"]} — {workload["title"]}", "nanoseconds per operation", lowerIsBetter: true, labelWidth: 200);

        foreach (var row in rows.Where(row => row.IsSupported)
                     .OrderBy(row => parameter.Length == 0 ? 0 : double.Parse(row.Parameter(parameter)!, CultureInfo.InvariantCulture))
                     .ThenBy(row => order.ToList().IndexOf(row.Parameter("Adapter")!)))
        {
            var adapter = row.Parameter("Adapter")!;
            chart.Add(adapter, row.MeanNs, order.ToList().IndexOf(adapter), row.MarginNs, parameter.Length == 0 ? string.Empty : $"{parameter} = {row.Parameter(parameter)}", floor: adapter == "Floor");
        }

        if (chart.Count == 0)
        {
            return null;
        }

        var file = $"{workload["id"].ToLowerInvariant()}-{workload["suite"].Replace("Benchmarks", string.Empty, StringComparison.Ordinal).Replace("Market", string.Empty, StringComparison.Ordinal).ToLowerInvariant()}.svg";
        File.WriteAllText(Path.Combine(directory, file), chart.Render());
        return new Chart(file, $"{workload["id"]} — {workload["title"]}", "Lower is better; overlapping whiskers are no measurable difference.");
    }

    private static Chart? Allocation(string directory, IReadOnlyList<IReadOnlyDictionary<string, string>> workloads, IReadOnlyList<ResultRow> cells, IReadOnlyList<string> order)
    {
        var chart = new BarChart("Allocation per operation — every workload", "bytes per operation", lowerIsBetter: true, labelWidth: 200);

        foreach (var workload in workloads)
        {
            var parameter = workload["parameter"];

            foreach (var row in cells.Where(row => row.SuiteName == workload["suite"] && row.IsSupported)
                         .OrderBy(row => parameter.Length == 0 ? 0 : double.Parse(row.Parameter(parameter)!, CultureInfo.InvariantCulture))
                         .ThenBy(row => order.ToList().IndexOf(row.Parameter("Adapter")!)))
            {
                var adapter = row.Parameter("Adapter")!;
                var group = parameter.Length == 0 ? workload["id"] : $"{workload["id"]}, {parameter} = {row.Parameter(parameter)}";
                chart.Add(adapter, row.AllocatedBytes, order.ToList().IndexOf(adapter), group: group, floor: adapter == "Floor");
            }
        }

        if (chart.Count == 0)
        {
            return null;
        }

        File.WriteAllText(Path.Combine(directory, "allocation.svg"), chart.Render());
        return new Chart("allocation.svg", "Allocation per operation", "Deterministic on any machine under the same runtime.");
    }
}
