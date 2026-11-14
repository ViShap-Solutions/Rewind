using System.Globalization;
using System.Text.Json;
using ViShap.Rewind.Benchmarks.Suites;

namespace ViShap.Rewind.Benchmarks.Reporting;

/// <summary>
/// Turns the raw files of a run into <c>report.md</c>, <c>report.html</c> and its charts.
/// </summary>
/// <remarks>
/// It reads the directory and nothing else, so the same command regenerates the same report from a baseline committed
/// long ago. A cell that has no number carries its state rather than a blank, and nothing here reduces the run to a
/// winner: Track A has no second library, and Track B compares only inside a tier.
/// </remarks>
internal static class ReportWriter
{
    /// <summary>The ceiling on a sub-microsecond cell's relative margin of error before it is called noisy.</summary>
    private const double MicroErrorCeiling = 3.0;

    /// <summary>The ceiling on a heavier cell, and the scale from which a cell is heavier.</summary>
    private const double HeavyErrorCeiling = 5.0;

    private const double HeavyThresholdNs = 1_000;

    /// <summary>The workloads of Track A in report order: id, suites, heading, and what the cell measures and why.</summary>
    private static readonly (string Id, string[] Suites, string Heading, string Note)[] Workloads =
    [
        ("W1", ["NoScopeWriteBenchmarks"], "W1 — a write with no scope",
            "INV-01 and contract §16: with no current scope a tracked write is a plain write and one check. The plain rows are the floor."),
        ("W2, W3", ["ScopeWriteBenchmarks"], "W2, W3 — a scope with N writes, commit and rollback",
            "Contract §4–§5: only the first write per cell in a scope records. `Distinct` writes N cells, each recorded; `One` writes one cell N times, recorded once."),
        ("W4", ["NestingBenchmarks"], "W4 — nesting depth D",
            "Contract §4.3: a nested scope is a mark in its outermost scope's journal. One write per level; every level commits, the innermost rolls back, or the outermost does."),
        ("W5", ["RunFormBenchmarks"], "W5 — the forms of opening a scope",
            "Contract §3.1: one committed write through each form. The lambdas are static; the struct operation is not boxed."),
        ("W6", ["EffectBenchmarks"], "W6 — K effects",
            "Contract §6: registration and execution of `AfterCommit` and of `Effect`, committed and compensated, against an empty scope."),
        ("W7", ["CollectionBenchmarks", "ClearRollbackBenchmarks"], "W7 — collections",
            "Contract §8: each mutation in a scope records its inverse; the rollback of `Clear` reattaches the detached storage, O(1) in the size cleared. `PlainListAdd` is the floor."),
        ("W8", ["PropertyBenchmarks"], "W8 — generated property, cell and plain property",
            "Contract §9: a `[Rewindable]` partial property against a `RewindCell<T>` and a plain property, with no scope and in a committed scope."),
        ("W9", ["AsyncScopeBenchmarks"], "W9 — an asynchronous scope",
            "Contract §13: `RunAsync` with a block that completes at once and one that yields, against the synchronous `Run`."),
        ("W9, W10", ["CellWriteBenchmarks"], "W9, W10 — a tracked write under the default job",
            "The cells PERF-01, PERF-02 and W10 are recorded against, measured under BenchmarkDotNet's default job so they stay comparable."),
        ("W15", ["NotificationBenchmarks"], "W15 — notifications with a subscriber",
            "Contract §8.6, §10.2: a list changed with no scope against an `ObservableCollection<T>` (the floor), a list changed in a committed scope (one reset), a notifying property committed and rolled back (one notification each)."),
        ("W11", ["ScopeSizeScalingBenchmarks"], "W11 — scope size",
            "The curve of W2 and W3 over N = 1 … 4096 distinct writes."),
        ("W12", ["DepthScalingBenchmarks"], "W12 — nesting depth",
            "The curve of W4 over D = 1 … 64."),
        ("W13", ["ClearScalingBenchmarks"], "W13 — rollback of Clear against size",
            "The O(1) of contract §8.3 as a curve, for a list and a dictionary of 10 … 1 000 000 items."),
    ];

    /// <summary>The cells the headline table picks, each as suite, method and parameters.</summary>
    private static readonly (string Label, string Suite, string Method, string Parameters)[] Headline =
    [
        ("W1 write, no scope, int", "NoScopeWriteBenchmarks", "CellInt", ""),
        ("W1 plain field, int (floor)", "NoScopeWriteBenchmarks", "PlainInt", ""),
        ("W1 write, no scope, string", "NoScopeWriteBenchmarks", "CellString", ""),
        ("W2 commit, 1 write", "ScopeWriteBenchmarks", "Commit", "Writes=1; Cells=Distinct"),
        ("W2 commit, 256 writes, distinct cells", "ScopeWriteBenchmarks", "Commit", "Writes=256; Cells=Distinct"),
        ("W2 commit, 256 writes, one cell", "ScopeWriteBenchmarks", "Commit", "Writes=256; Cells=One"),
        ("W3 rollback, 256 writes, distinct cells", "ScopeWriteBenchmarks", "Rollback", "Writes=256; Cells=Distinct"),
        ("W4 depth 16, commit all", "NestingBenchmarks", "CommitAll", "Depth=16"),
        ("W4 depth 16, rollback outermost", "NestingBenchmarks", "RollbackOutermost", "Depth=16"),
        ("W5 Begin/Commit", "RunFormBenchmarks", "BeginCommit", ""),
        ("W5 Run(lambda)", "RunFormBenchmarks", "RunLambda", ""),
        ("W5 Run(struct operation)", "RunFormBenchmarks", "RunStructOperation", ""),
        ("W6 16 AfterCommit run", "EffectBenchmarks", "AfterCommitRun", "Effects=16"),
        ("W6 16 Effect compensated", "EffectBenchmarks", "EffectCompensated", "Effects=16"),
        ("W7 256 RewindList.Add, commit", "CollectionBenchmarks", "ListAddCommit", "Mutations=256"),
        ("W7 256 List.Add (floor)", "CollectionBenchmarks", "PlainListAdd", "Mutations=256"),
        ("W7 rollback of Clear, 100 000 items", "ClearRollbackBenchmarks", "ClearRollback", "Size=100000"),
        ("W8 generated property, no scope", "PropertyBenchmarks", "GeneratedProperty", ""),
        ("W8 plain property (floor)", "PropertyBenchmarks", "PlainProperty", ""),
        ("W9 RunAsync, completed", "AsyncScopeBenchmarks", "RunAsyncCompleted", ""),
        ("W9 RunAsync, yielding", "AsyncScopeBenchmarks", "RunAsyncYielding", ""),
        ("W9 Run", "AsyncScopeBenchmarks", "SyncRun", ""),
    ];

    internal static string Generate(string directory)
    {
        var run = RunRecord.Read(Path.Combine(directory, "run.json"));
        var results = File.Exists(Path.Combine(directory, "results.csv")) ? ResultRow.Read(Path.Combine(directory, "results.csv")) : [];
        var document = new Document();
        var title = run.IsMarket ? "Track B" : "Track A";

        Identity(document, run, directory, title);

        if (run.IsMarket)
        {
            MarketReport.Write(document, directory, results);
        }
        else
        {
            TrackA(document, run, directory, results);
        }

        var markdown = document.ToMarkdown();
        File.WriteAllText(Path.Combine(directory, "report.md"), markdown);
        File.WriteAllText(Path.Combine(directory, "report.html"), document.ToHtml($"ViShap.Rewind {title} — {run.Label}"));

        return markdown;
    }

    /// <summary>
    /// The report generated, snapshotted, generated again and compared byte for byte. A generator that depended on
    /// anything besides the raw files — a hash randomized per process, a timestamp — fails here.
    /// </summary>
    /// <returns>The files that differed between the two generations; empty when the report is a view.</returns>
    internal static IReadOnlyList<string> GenerateAndCheck(string directory)
    {
        Generate(directory);
        var first = Snapshot(directory);

        Generate(directory);
        var second = Snapshot(directory);

        return [.. first.Keys.Where(name => !second.TryGetValue(name, out var bytes) || !bytes.SequenceEqual(first[name])).Order(StringComparer.Ordinal)];
    }

    private static Dictionary<string, byte[]> Snapshot(string directory)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var name in new[] { "report.md", "report.html" })
        {
            var path = Path.Combine(directory, name);

            if (File.Exists(path))
            {
                files[name] = File.ReadAllBytes(path);
            }
        }

        var charts = Path.Combine(directory, "charts");

        if (Directory.Exists(charts))
        {
            foreach (var path in Directory.EnumerateFiles(charts))
            {
                files[$"charts/{Path.GetFileName(path)}"] = File.ReadAllBytes(path);
            }
        }

        return files;
    }

    private static void TrackA(Document document, RunRecord run, string directory, IReadOnlyList<ResultRow> results)
    {
        var chartsDirectory = Path.Combine(directory, "charts");
        var charts = Charts.Write(chartsDirectory, results).ToList();
        var cold = Csv.Read(Path.Combine(directory, "cold-start.csv"));
        var soak = Csv.Read(Path.Combine(directory, "soak.csv"));

        if (Charts.ColdStart(chartsDirectory, cold) is { } coldChart)
        {
            charts.Add(coldChart);
        }

        charts.AddRange(Charts.Soak(chartsDirectory, soak));

        Caveats(document, run);
        HeadlineTable(document, results);
        Environment(document, directory);
        Verification(document, directory);

        foreach (var (_, suites, heading, note) in Workloads)
        {
            Suite(document, results, heading, note, suites);
        }

        Concurrency(document, results);
        ColdStart(document, cold);
        Soak(document, soak);
        Gallery(document, charts);
        Caution(document, results);
    }

    private static void Identity(Document document, RunRecord run, string directory, string title)
    {
        document.Heading(1, $"ViShap.Rewind {title} — {(run.IsBaseline ? "baseline" : "measurement")} {run.Label}");

        document.Paragraph(run.IsBaseline
            ? "A frozen record of one tagged revision. It is never regenerated from new measurements: a rebuilt baseline agrees with whatever the code became, and so proves nothing."
            : $"**Not a baseline.** {run.Reason}. It carries only what it measured, and every figure in it belongs to this run alone.");

        if (run.Interrupted)
        {
            document.Paragraph(
                $"**Interrupted** by {run.StoppedBy}. {run.SuitesRun.Count} suite(s) finished and are reported below; what did not run is listed in `scope.md`.");
        }

        document.Table(
            ["Field", "Value"],
            [
                ["Kind", run.Kind],
                ["Track", run.Track],
                [
                    "Tag",
                    (run.Tag, run.MeasuredAs) switch
                    {
                        (null, _) => "none — this commit is not tagged",
                        (_, null) => run.Tag,
                        _ => $"{run.MeasuredAs}, measured from the revision below, whose `src/` differs from the tag only in "
                             + (run.SourceDifferences is { Count: > 0 } files ? string.Join(", ", files.Select(file => $"`{file}`")) : "nothing")
                             + " — no code",
                    }
                ],
                ["git describe", $"`{run.Describe}`"],
                ["Revision", $"`{run.Revision}`"],
                [
                    "Working tree",
                    run.Dirty
                        ? run.AllowDirty ? "**dirty**, allowed by `--allow-dirty`: the code measured is not exactly the code the revision names" : "**dirty**"
                        : "clean — the code measured is the code the revision names"
                ],
                ["Job", run.Job],
                ["Filter", run.Filter is null ? "none — every suite of the track" : $"`{run.Filter}`"],
                ["Started (UTC)", run.StartedUtc],
                ["Finished (UTC)", run.FinishedUtc],
                ["Elapsed", Runners.TrackRunner.Elapsed(run).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)],
                ["Directory", $"`{Path.GetFileName(directory)}`"],
            ]);

        document.Heading(2, "How this run was produced");
        document.Code(string.Join("\n", run.Commands));
    }

    private static void Caveats(Document document, RunRecord run)
    {
        document.Heading(2, "What this record is, and what it is not");

        document.Bullets(
        [
            "**No second library was measured.** This is Track A: Rewind against plain code doing the same work with no rollback. Track B compares Rewind with the alternatives, inside a tier.",
            "**The floor is plain code.** Rows named `Plain…` do the same writes with nothing to undo; a Rewind cell is read against them, and a cell near the floor is a harness artifact, not a fast mechanism.",
            "**Every timing belongs to the machine below.** It is not portable and not comparable with a figure taken anywhere else. **Allocation per operation does not depend on the machine** under the same runtime.",
            "**Two intervals that overlap are no measurable difference.** Every mean is printed with its 99.9 % margin of error; a ratio is never read without the margins beside it.",
            "**No adjective.** These numbers support a claim only through the cell that carries it; the plan's claim gate decides what may be said where.",
            run.SuitesNotRun.Count == 0 ? "**Every suite and step of the track ran.**" : $"**{run.SuitesNotRun.Count} suite(s) or step(s) did not run**, listed in `scope.md`.",
        ]);
    }

    private static void HeadlineTable(Document document, IReadOnlyList<ResultRow> results)
    {
        var rows = new List<IReadOnlyList<string>>();

        foreach (var (label, suite, method, parameters) in Headline)
        {
            var row = results.FirstOrDefault(row => row.SuiteName == suite && row.Method == method && row.Parameters == parameters);

            if (row is not null)
            {
                rows.Add([label, Format.Interval(row), Format.Bytes(row.AllocatedBytes)]);
            }
        }

        if (rows.Count == 0)
        {
            return;
        }

        document.Heading(2, "Headline cells");
        document.Paragraph("The cells the plan records as the position of a version, picked from the tables below. Mean ± 99.9 % margin of error; allocation per operation.");
        document.Table(["Cell", "Time", "Allocated"], rows);
    }

    internal static void Environment(Document document, string directory)
    {
        var path = Path.Combine(directory, "environment.json");

        if (!File.Exists(path))
        {
            return;
        }

        document.Heading(2, "Environment");

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var rows = new List<IReadOnlyList<string>>();

        foreach (var property in json.RootElement.EnumerateObject())
        {
            if (property.Name is "packages" or "packagesOnNuGet" or "libraries" or "machine" or "runtimeSettings")
            {
                continue;
            }

            rows.Add([property.Name, property.Value.ToString()]);
        }

        if (json.RootElement.TryGetProperty("machine", out var machine) && machine.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in machine.EnumerateObject())
            {
                rows.Add([$"machine.{property.Name}", property.Value.ToString()]);
            }
        }

        if (json.RootElement.TryGetProperty("runtimeSettings", out var settings) && settings.ValueKind == JsonValueKind.Object)
        {
            rows.Add(["runtimeSettings", settings.EnumerateObject().Any() ? string.Join(", ", settings.EnumerateObject().Select(setting => $"{setting.Name}={setting.Value}")) : "all at their defaults"]);
        }

        document.Table(["Field", "Value"], rows);

        if (json.RootElement.TryGetProperty("libraries", out var libraries))
        {
            document.Heading(3, "Libraries as loaded");
            document.Table(["Assembly", "Version"], [.. libraries.EnumerateObject().Select(library => (IReadOnlyList<string>)[library.Name, $"`{library.Value}`"])]);
        }

        if (json.RootElement.TryGetProperty("packages", out var packages) && packages.EnumerateObject().Any())
        {
            json.RootElement.TryGetProperty("packagesOnNuGet", out var currency);

            document.Heading(3, "Pinned packages");
            document.Table(
                ["Package", "Pinned", "Latest stable of the same major on the day"],
                [.. packages.EnumerateObject().Select(package => (IReadOnlyList<string>)
                [
                    package.Name,
                    package.Value.ToString(),
                    currency.ValueKind == JsonValueKind.Object && currency.TryGetProperty(package.Name, out var entry) && entry.TryGetProperty("Latest", out var latest)
                        ? latest.ToString() is { Length: > 0 } text ? text : "unknown"
                        : "not checked",
                ])]);
        }
    }

    private static void Verification(Document document, string directory)
    {
        var cases = Csv.Read(Path.Combine(directory, "verification.csv"));

        if (cases.Count == 0)
        {
            return;
        }

        document.Heading(2, "Verification before timing");
        document.Paragraph(
            $"{cases.Count} cases — every benchmark under every combination of its parameters — were set up, invoked once and cleaned up before any timing, and afterwards no scope was allowed to be open. A benchmark that leaves a scope open measures every later invocation inside it.");
        document.Table(["State", "Cases"], [.. cases.GroupBy(@case => @case["state"]).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => (IReadOnlyList<string>)[group.Key, group.Count().ToString(CultureInfo.InvariantCulture)])]);
    }

    private static readonly string[] Headers = ["Method", "Parameters", "Mean ± margin", "Margin", "Median", "P95", "Allocated", "Gen0/1k ops"];

    private static IReadOnlyList<string> Cells(ResultRow row) =>
    [
        Charts.IsFloor(row) ? $"{row.Method} (floor)" : row.Method,
        row.Parameters.Length == 0 ? "—" : row.Parameters,
        Format.Interval(row),
        row.IsSupported ? Format.Percent(row.RelativeErrorPercent) : "—",
        Format.Time(row.MedianNs),
        Format.Time(row.P95Ns),
        Format.Bytes(row.AllocatedBytes),
        Format.Decimal(row.Gen0Per1000),
    ];

    private static void Suite(Document document, IReadOnlyList<ResultRow> results, string heading, string note, IReadOnlyList<string> suites)
    {
        var rows = results.Where(row => suites.Contains(row.SuiteName, StringComparer.Ordinal)).ToList();

        if (rows.Count == 0)
        {
            return;
        }

        document.Heading(2, heading);
        document.Paragraph(note);

        foreach (var suite in rows.GroupBy(row => row.SuiteName).OrderBy(group => suites.ToList().IndexOf(group.Key)))
        {
            if (suites.Count > 1)
            {
                document.Heading(3, suite.Key);
            }

            document.Table(Headers, [.. suite.Select(Cells)]);
        }
    }

    private static void Concurrency(Document document, IReadOnlyList<ResultRow> results)
    {
        var rows = results.Where(row => row.SuiteName == "ConcurrencyBenchmarks").ToList();

        if (rows.Count == 0)
        {
            return;
        }

        document.Heading(2, "W14 — independent scopes on many threads");
        document.Paragraph(
            $"Every thread opens {ConcurrencyBenchmarks.ScopesPerThread.ToString("N0", CultureInfo.InvariantCulture)} scopes of four writes to cells of its own. Nothing is shared but the engine; the time of an invocation is the time of the slowest thread. Scaling is the aggregate throughput over T times the single-thread throughput of the same method.");

        var table = new List<IReadOnlyList<string>>();

        foreach (var row in rows)
        {
            var single = rows.FirstOrDefault(other => other.Method == row.Method && other.Parameter("Threads") == "1");
            var threads = double.Parse(row.Parameter("Threads") ?? "1", CultureInfo.InvariantCulture);
            var scaling = single is { MeanNs: > 0 } && row.MeanNs > 0 ? Charts.Throughput(row) / (Charts.Throughput(single) * threads) : double.NaN;

            table.Add(
            [
                row.Method,
                row.Parameter("Threads") ?? "—",
                Format.Interval(row),
                Format.Count(Charts.Throughput(row)),
                double.IsNaN(scaling) ? "—" : $"{(scaling * 100).ToString("F0", CultureInfo.InvariantCulture)} %",
                Format.Bytes(row.AllocatedBytes),
            ]);
        }

        document.Table(["Method", "Threads", "Invocation", "Scopes/s, all threads", "Scaling", "Allocated"], table);
    }

    private static void ColdStart(Document document, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        document.Heading(2, "Cold start");
        document.Paragraph(
            "One process per measurement: the child performs one operation and exits, so the type loading, static constructors and JIT of the path are in the number. `empty` does nothing and shows the start-up every row pays besides its operation.");
        document.Table(
            ["Scenario", "Operation", "Launches", "Start-up (median)", "First operation (median)", "Min", "P95", "Max", "Allocated (median)"],
            [.. rows.Select(row => (IReadOnlyList<string>)
            [
                row["scenario"], row["description"], row["launches"], $"{row["startup_ms_median"]} ms",
                $"{row["first_operation_us_median"]} µs", $"{row["first_operation_us_min"]} µs", $"{row["first_operation_us_p95"]} µs",
                $"{row["first_operation_us_max"]} µs", Format.Bytes(double.Parse(row["allocated_bytes_median"], CultureInfo.InvariantCulture)),
            ])]);
    }

    private static void Soak(Document document, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        double Value(IReadOnlyDictionary<string, string> row, string column) => double.Parse(row[column], CultureInfo.InvariantCulture);

        var throughput = rows.Select(row => Value(row, "ops_per_second")).ToList();
        var heap = rows.Select(row => Value(row, "managed_heap_bytes")).ToList();

        document.Heading(2, "Sustained load");
        document.Paragraph(
            "A mixed workload — a committed scope over 16 cells with an effect, a rolled-back scope with a nested committed one, a committed collection change and a rolled-back `Clear` — run without pause, sampled every five seconds.");
        document.Table(
            ["Measure", "Value"],
            [
                ["Duration", $"{rows[^1]["elapsed_s"]} s, {rows.Count} samples"],
                ["Scopes", Format.Count(Value(rows[^1], "operations"))],
                ["Throughput, first / last / lowest sample", $"{Format.Count(throughput[0])} / {Format.Count(throughput[^1])} / {Format.Count(throughput.Min())} scopes/s"],
                ["Managed heap, first / last / highest sample", $"{Format.Bytes(heap[0])} / {Format.Bytes(heap[^1])} / {Format.Bytes(heap.Max())}"],
                ["Gen0 / Gen1 / Gen2 collections at the end", $"{rows[^1]["gen0"]} / {rows[^1]["gen1"]} / {rows[^1]["gen2"]}"],
            ]);
    }

    private static void Gallery(Document document, IReadOnlyList<Chart> charts)
    {
        if (charts.Count == 0)
        {
            return;
        }

        document.Heading(2, "Charts");
        document.Paragraph("Generated from the files above and from nothing else. Every chart names its unit and whether lower or higher is better; whiskers are the 99.9 % interval.");

        foreach (var chart in charts)
        {
            document.Image($"charts/{chart.File}", $"{chart.Title} — {chart.Note}");
        }
    }

    /// <summary>
    /// The cells a reader should not take at face value: one whose margin of error exceeds the ceiling, and one that
    /// produced no number at all.
    /// </summary>
    internal static void Caution(Document document, IReadOnlyList<ResultRow> results)
    {
        var failed = results.Where(row => !row.IsSupported).ToList();

        // The floor is a fraction of a nanosecond, where a relative margin says nothing about the measurement.
        var noisy = results
            .Where(row => row.IsSupported && row.MeanNs >= 1 && !Charts.IsFloor(row))
            .Where(row => row.RelativeErrorPercent > (row.MeanNs >= HeavyThresholdNs ? HeavyErrorCeiling : MicroErrorCeiling))
            .OrderByDescending(row => row.RelativeErrorPercent)
            .ToList();

        document.Heading(2, "Cells to read with caution");

        if (failed.Count == 0 && noisy.Count == 0)
        {
            document.Paragraph(
                "None. Every cell produced a number, and every margin of error is inside the ceiling: 3 % below a microsecond, 5 % above.");
            return;
        }

        if (failed.Count > 0)
        {
            document.Heading(3, "Produced no number");
            document.Table(["Suite", "Method", "Parameters", "State"],
                [.. failed.Select(row => (IReadOnlyList<string>)[row.SuiteName, row.Method, row.Parameters, row.State])]);
        }

        if (noisy.Count > 0)
        {
            document.Heading(3, "Noisy");
            document.Paragraph("Published with the margin the run produced, and never re-run until the number looked better. A claim may not rest on one of these alone.");
            document.Table(["Suite", "Method", "Parameters", "Mean", "Margin"],
                [.. noisy.Select(row => (IReadOnlyList<string>)[row.SuiteName, row.Method, row.Parameters, Format.Time(row.MeanNs), Format.Percent(row.RelativeErrorPercent)])]);
        }
    }
}
