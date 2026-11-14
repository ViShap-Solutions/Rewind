using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using ViShap.Rewind.Benchmarks.Config;
using ViShap.Rewind.Benchmarks.Environment;
using ViShap.Rewind.Benchmarks.Market;
using ViShap.Rewind.Benchmarks.Market.Suites;
using ViShap.Rewind.Benchmarks.Reporting;
using ViShap.Rewind.Benchmarks.Suites;

namespace ViShap.Rewind.Benchmarks.Runners;

/// <summary>
/// The one command of a track: it verifies, runs every suite of the track in order, writes every artifact into the
/// run's own directory, generates the report and stops. There is no interactive step and no decision in the middle.
/// </summary>
/// <remarks>
/// Where it writes is decided by git and by the shape of the run, never by a flag that names a version. A tagged
/// commit measured in full under the publication job produces a baseline; anything else produces a measurement under
/// <c>Measurements/</c>, which says in <c>scope.md</c> what it did not measure.
/// </remarks>
internal static class TrackRunner
{
    /// <summary>Every timed suite of Track A, in the order a run measures them. The order is part of the record.</summary>
    private static readonly (string Stage, Type Suite)[] TrackA =
    [
        // A1 — the mechanism record, W1–W10.
        ("a1", typeof(NoScopeWriteBenchmarks)),
        ("a1", typeof(ScopeWriteBenchmarks)),
        ("a1", typeof(NestingBenchmarks)),
        ("a1", typeof(RunFormBenchmarks)),
        ("a1", typeof(EffectBenchmarks)),
        ("a1", typeof(CollectionBenchmarks)),
        ("a1", typeof(ClearRollbackBenchmarks)),
        ("a1", typeof(PropertyBenchmarks)),
        ("a1", typeof(AsyncScopeBenchmarks)),
        ("a1", typeof(CellWriteBenchmarks)),

        // A2 — the scaling curves, W11–W13.
        ("a2", typeof(ScopeSizeScalingBenchmarks)),
        ("a2", typeof(DepthScalingBenchmarks)),
        ("a2", typeof(ClearScalingBenchmarks)),

        // A3 — independent scopes on many threads, W14. Cold start and the soak are process-level and follow.
        ("a3", typeof(ConcurrencyBenchmarks)),
    ];

    /// <summary>The comparative suites of Track B, in the order of their workloads.</summary>
    private static readonly (string Stage, Type Suite)[] TrackB =
    [
        ("b1", typeof(MarketCommitBenchmarks)),
        ("b1", typeof(MarketRollbackBenchmarks)),
        ("b1", typeof(MarketReadBenchmarks)),
        ("b1", typeof(MarketWriteOutsideBenchmarks)),
        ("b1", typeof(MarketNestedBenchmarks)),
        ("b1", typeof(MarketListReplaceBenchmarks)),
        ("b1", typeof(MarketListRollbackBenchmarks)),
        ("b1", typeof(MarketEffectBenchmarks)),
    ];

    /// <summary>The names of both tracks' suites, in the order a run measures them.</summary>
    internal static IReadOnlyList<string> SuiteOrder { get; } = [.. TrackA.Concat(TrackB).Select(entry => entry.Suite.Name)];

    /// <summary>Every suite of Track A, for the modes that run them outside a track.</summary>
    internal static IReadOnlyList<Type> TrackASuites { get; } = [.. TrackA.Select(entry => entry.Suite)];

    internal static int Run(string[] args)
    {
        bool market = string.Equals(Value(args, "--track"), "B", StringComparison.OrdinalIgnoreCase);
        var track = market ? TrackB : TrackA;
        var letter = market ? "B" : "A";

        bool shortened = args.Contains("--short", StringComparer.Ordinal);
        bool allowDirty = args.Contains("--allow-dirty", StringComparer.Ordinal);
        string? filter = Value(args, "--filter");
        string? asTag = Value(args, "--as");
        double soakMinutes = Number(args, "--soak", shortened ? 0 : 5);
        int launches = (int)Number(args, "--launches", shortened ? 5 : 20);

        var unregistered = Unregistered();

        if (unregistered.Count > 0)
        {
            Console.Error.WriteLine("These suites carry benchmarks but belong to no track, so a run would silently leave them out:");

            foreach (var name in unregistered)
            {
                Console.Error.WriteLine($"  {name}");
            }

            return 1;
        }

        var target = RunTarget.Decide(shortened, filter, allowDirty, market, asTag);
        var selected = Select(filter, track);

        if (selected.Count == 0)
        {
            Console.Error.WriteLine($"No suite of Track {letter} matches '{filter}'. Nothing was written.");
            return 1;
        }

        if (args.Contains("--list", StringComparer.Ordinal))
        {
            Console.WriteLine($"{selected.Count} of {track.Length} suite(s) would run:");

            foreach (var entry in track.Where(entry => selected.Contains(entry.Suite)))
            {
                Console.WriteLine($"  {entry.Stage}  {entry.Suite.Name}");
            }

            Console.WriteLine();
            Console.WriteLine($"Destination would be {target.Directory}");
            Console.WriteLine($"Kind would be {target.Kind} — {target.Reason}");

            if (target.IsBaseline && target.Dirty && !target.AllowDirty)
            {
                Console.WriteLine("It would refuse: the working tree has uncommitted changes, so the baseline would not measure what its tag points at.");
            }

            Console.WriteLine("Nothing was measured and nothing was written.");
            return 0;
        }

        try
        {
            target.Create();
        }
        catch (InvalidOperationException refusal)
        {
            Console.Error.WriteLine(refusal.Message);
            return 1;
        }

        var startedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var command = $"dotnet run --project {Paths.ProjectName} -c Release -- --track {letter}"
                      + (shortened ? " --short" : string.Empty)
                      + (filter is null ? string.Empty : $" --filter '{filter}'")
                      + (asTag is null ? string.Empty : $" --as {asTag}")
                      + (!market && !shortened && soakMinutes is not 5 ? $" --soak {soakMinutes.ToString(CultureInfo.InvariantCulture)}" : string.Empty)
                      + (allowDirty ? " --allow-dirty" : string.Empty);

        var notSelected = track.Select(entry => entry.Suite).Except(selected).Select(suite => suite.Name).ToList();
        var revision = Shell.Git("rev-parse HEAD");
        var job = shortened ? "Shortened" : "Publication";

        RunRecord Record(RunTarget written, IReadOnlyList<string> run, IReadOnlyList<string> notRun, bool interrupted = false,
            string? stoppedBy = null, IReadOnlyList<string>? unfinished = null, string finishedUtc = "") =>
            new(
                Kind: written.Kind.ToString(),
                Label: written.Label,
                Tag: written.Tag,
                Describe: written.Describe,
                Revision: revision,
                Dirty: written.Dirty,
                AllowDirty: written.AllowDirty,
                Reason: written.Reason,
                Track: market ? RunRecord.TrackB : RunRecord.TrackA,
                Job: job,
                Filter: filter,
                StartedUtc: startedUtc,
                FinishedUtc: finishedUtc,
                Commands: [command],
                SuitesRun: run,
                SuitesNotRun: [.. notSelected, .. notRun],
                Interrupted: interrupted,
                StoppedBy: stoppedBy,
                SuitesUnfinished: unfinished,
                MeasuredAs: written.MeasuredAs,
                SourceDifferences: written.MeasuredAs is null ? null : written.SourceDifferences);

        using var stop = StopSignal.Listen(target.WorkingDirectory, () => StopAtOnce(target));

        Announce(letter, target, selected, job, filter, market ? 0 : soakMinutes, stop);

        // The manifest first, so even a run that fails later says what it ran on.
        Step("manifest", () => EnvironmentManifest.Write(target.File("environment.json")));

        // Verification before any timing: a timing over an unverified case is not a measurement.
        if (!(market ? VerifyMarket(target) : VerifyTrackA(target, selected)))
        {
            return 1;
        }

        var summaries = new List<Summary>();
        var units = Units(target, selected, shortened, market ? 0 : soakMinutes, launches, summaries, processSteps: !market && filter is null);
        var done = new List<Unit>();
        string? stoppedBy = null;

        foreach (var unit in units)
        {
            if (stop.Requested)
            {
                stoppedBy = stop.How;
                break;
            }

            // While a unit runs, the record on disk is the one a process that dies would leave.
            Record(target.Interrupted(), Suites(done), Names(units.Skip(done.Count + 1)), interrupted: true, unfinished: [unit.Name])
                .Write(target.File("run.json"));

            unit.Work();
            done.Add(unit);
        }

        stop.Dispose();

        if (stoppedBy is not null)
        {
            var stopped = target.Interrupted();

            Console.WriteLine();
            Console.WriteLine($"── Stopped by {stoppedBy} after {done.Count} of {units.Count} unit(s). Writing what was measured.");

            File.Delete(stopped.File("STOP"));

            return Finish(
                stopped.WorkingDirectory,
                Record(stopped, Suites(done), Names(units.Skip(done.Count)), interrupted: true, stoppedBy: stoppedBy, unfinished: [],
                    finishedUtc: DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                ResultRow.From(summaries));
        }

        try
        {
            target = target.Settle();
        }
        catch (InvalidOperationException refusal)
        {
            Console.Error.WriteLine(refusal.Message);
            target = target.Interrupted() with { Reason = "a baseline of the same tag appeared while this run measured" };
        }

        return Finish(
            target.WorkingDirectory,
            Record(target, [.. selected.Select(suite => suite.Name)], [], finishedUtc: DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            ResultRow.From(summaries));
    }

    /// <summary>
    /// <c>--report</c> on a run with no <c>results.csv</c>: the process ended before it wrote one. The cells come back
    /// from BenchmarkDotNet's exports, the record is the one the run kept current while it worked, and what follows is
    /// exactly what a run stopped on request writes — always a measurement.
    /// </summary>
    internal static int Recover(string directory)
    {
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var baselines = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Paths.Baselines)) + Path.DirectorySeparatorChar;

        if (directory.StartsWith(baselines, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"{directory} is under Baselines/ and has no results.csv. A baseline is never rebuilt from its exports: a run " +
                "that did not finish is not one. Nothing was written.");
            return 1;
        }

        var exports = ExportRecovery.Read(directory, SuiteOrder);
        var path = Path.Combine(directory, "run.json");

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"{directory} holds no run.json, so nothing says what the run was. Nothing was written.");
            return 1;
        }

        var kept = RunRecord.Read(path);
        var exported = exports.Select(export => export.Suite).ToHashSet(StringComparer.Ordinal);
        var finished = exports.Count > 0 ? exports.Max(export => export.WrittenUtc) : Directory.GetLastWriteTimeUtc(directory);

        var record = kept with
        {
            Kind = nameof(RunKind.Measurement),
            Label = Path.GetFileName(directory),
            Reason = kept.Interrupted ? kept.Reason : $"{kept.Reason}; the run was interrupted",
            FinishedUtc = finished.ToString("O", CultureInfo.InvariantCulture),
            SuitesRun = [.. kept.SuitesRun.Concat(exported).Distinct(StringComparer.Ordinal).OrderBy(suite => SuiteOrder.ToList().IndexOf(suite))],
            SuitesNotRun = [.. kept.SuitesNotRun.Where(name => !exported.Contains(name))],
            SuitesUnfinished = [.. (kept.SuitesUnfinished ?? []).Where(name => !exported.Contains(name))],
            Interrupted = true,
            StoppedBy = kept.StoppedBy ?? "the end of the process, before the run finished",
        };

        Console.WriteLine($"No results.csv in {directory}: rebuilding it from {exports.Count} BenchmarkDotNet export(s).");

        return Finish(directory, record, [.. exports.SelectMany(export => export.Rows)]);
    }

    private static bool VerifyTrackA(RunTarget target, IReadOnlyList<Type> selected)
    {
        int failed = 0;

        Step("verify — every benchmark invoked once, no scope left open", () =>
        {
            var cases = TrackAVerifier.VerifyAll(selected);
            TrackAVerifier.Write(cases, target.File("verification.csv"));
            failed = cases.Count(@case => @case.State != ResultRow.Supported);

            foreach (var @case in cases.Where(@case => @case.State != ResultRow.Supported))
            {
                Console.WriteLine($"   {@case.Suite}.{@case.Method} ({@case.Parameters}): {@case.Detail}");
            }

            Console.WriteLine($"{cases.Count} cases, {failed} failed.");
        });

        if (failed > 0)
        {
            Console.Error.WriteLine($"{failed} case(s) failed verification. No timing was taken: correctness comes first.");
            return false;
        }

        return true;
    }

    private static bool VerifyMarket(RunTarget target)
    {
        int failed = 0;

        Step("verify — probes, tiers and every timed pair", () =>
        {
            var verification = MarketVerifier.VerifyAll();
            MarketVerifier.Write(verification, target.WorkingDirectory);
            failed = verification.FailedCount;

            foreach (var probe in verification.Probes.Where(probe => probe.Mismatch))
            {
                Console.WriteLine($"   {probe.Adapter} {probe.Capability}: declared {probe.Declared}, probed {probe.State} — {probe.Detail}");
            }

            foreach (var pair in verification.Pairs.Where(pair => pair.State != MarketVerifier.Supported))
            {
                Console.WriteLine($"   {pair.Workload} {pair.Parameters}: {pair.State} — {pair.Detail}");
            }

            Console.WriteLine($"{verification.Probes.Count} probes, {verification.Pairs.Count} pairs, {failed} failed.");
        });

        if (failed > 0)
        {
            Console.Error.WriteLine($"{failed} probe or pair failure(s). No timing was taken: a timing over an unverified pair is not a measurement.");
            return false;
        }

        return true;
    }

    /// <summary>One thing a run measures between two chances to stop: a suite, or a process-level step.</summary>
    private sealed record Unit(string Name, Type? Suite, Action Work);

    private static List<Unit> Units(
        RunTarget target, IReadOnlyList<Type> selected, bool shortened, double soakMinutes, int launches, List<Summary> summaries, bool processSteps)
    {
        var units = new List<Unit>();

        for (var index = 0; index < selected.Count; index++)
        {
            var suite = selected[index];
            var position = $"[{index + 1}/{selected.Count}]";
            var kind = shortened ? JobKind.Shortened : suite.IsDefined(typeof(KeepsDefaultJobAttribute)) ? JobKind.Default : JobKind.Publication;

            units.Add(new Unit(suite.Name, suite, () =>
            {
                Console.WriteLine();
                Console.WriteLine($"── {position} {suite.Name}");

                summaries.Add(BenchmarkRunner.Run(suite, new BenchmarkConfig(target.WorkingDirectory, kind)));
            }));
        }

        if (!processSteps)
        {
            return units;
        }

        units.Add(StepUnit("cold start", () => ColdStartRunner.Drive(target.WorkingDirectory, launches)));

        if (soakMinutes > 0)
        {
            units.Add(StepUnit($"soak {soakMinutes.ToString("0.#", CultureInfo.InvariantCulture)} min",
                () => SoakRunner.Run(TimeSpan.FromMinutes(soakMinutes), target.WorkingDirectory)));
        }

        return units;
    }

    private static Unit StepUnit(string name, Action work) => new(name, null, () => Step(name, work));

    private static List<string> Suites(IEnumerable<Unit> units) => [.. units.Where(unit => unit.Suite is not null).Select(unit => unit.Name)];

    private static List<string> Names(IEnumerable<Unit> units) => [.. units.Select(unit => unit.Name)];

    private static void StopAtOnce(RunTarget target)
    {
        Console.WriteLine();
        Console.WriteLine("── Ctrl+C again: stopped at once.");
        Console.WriteLine($"   The suites that finished are exported in {target.WorkingDirectory}. To turn them into this run's results and report:");
        Console.WriteLine($"   dotnet run --project {Paths.ProjectName} -c Release -- --report \"{target.WorkingDirectory}\"");

        System.Environment.Exit(StopSignal.ImmediateExitCode);
    }

    /// <summary>
    /// Writes everything a run leaves behind, from its cells and its record: the archive of the export, the tables,
    /// <c>run.json</c>, the manifest, the reproduction instructions, the scope of a measurement and the report. A run
    /// that finished, one stopped on request and one rebuilt by <c>--report</c> all end here.
    /// </summary>
    private static int Finish(string directory, RunRecord record, IReadOnlyList<ResultRow> rows)
    {
        Step("archive the BenchmarkDotNet export", () => Archive(directory));

        ResultRow.Write(rows, Path.Combine(directory, "results.csv"));
        ResultRow.WriteMemory(rows, Path.Combine(directory, "memory.csv"));
        record.Write(Path.Combine(directory, "run.json"));
        Manifest(directory, record, rows);
        Reproduction(directory, record);

        if (!record.IsBaseline)
        {
            Scope(directory, record);
        }

        // The report is generated twice and compared, so a generator that stopped being a pure view over the raw files
        // says so here.
        var drifted = ReportWriter.GenerateAndCheck(directory);

        foreach (var file in drifted)
        {
            Console.Error.WriteLine($"{file} differs between two generations from the same raw files: the report is not a view over them. This is a harness defect.");
        }

        int failedCells = rows.Count(row => !row.IsSupported);

        Console.WriteLine();
        Console.WriteLine($"{record.Kind} {record.Label} — {rows.Count} cells, {failedCells} without a number.");
        Console.WriteLine($"Written to {directory}");
        Console.WriteLine($"Report: {Path.Combine(directory, "report.html")}");
        Console.WriteLine($"Elapsed {Elapsed(record):hh\\:mm\\:ss}");

        if (!record.IsBaseline)
        {
            Console.WriteLine($"Not a baseline: {record.Reason}.");
        }

        if (record.Interrupted)
        {
            Console.WriteLine($"Interrupted by {record.StoppedBy}: {record.SuitesRun.Count} suite(s) measured; what did not run is listed in scope.md.");
            return 2;
        }

        return failedCells == 0 && drifted.Count == 0 ? 0 : 1;
    }

    internal static TimeSpan Elapsed(RunRecord record) =>
        DateTime.TryParse(record.StartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started)
        && DateTime.TryParse(record.FinishedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var finished)
        && finished >= started
            ? finished - started
            : TimeSpan.Zero;

    /// <summary>
    /// Packs the BenchmarkDotNet export into one file beside the results it came from. Only a recovery reads it back;
    /// the report is built from <c>results.csv</c>. An archive that already exists keeps what it holds.
    /// </summary>
    private static void Archive(string directory)
    {
        var loose = ExportRecovery.LooseFiles(directory).ToList();

        if (loose.Count == 0)
        {
            return;
        }

        var archive = Path.Combine(directory, "results.zip");
        var partial = archive + ".partial";
        File.Delete(partial);

        using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
        {
            if (File.Exists(archive))
            {
                using var previous = ZipFile.OpenRead(archive);

                foreach (var entry in previous.Entries.Where(entry => loose.All(file => file.Key != entry.FullName)))
                {
                    using var source = entry.Open();
                    using var copy = zip.CreateEntry(entry.FullName, CompressionLevel.SmallestSize).Open();
                    source.CopyTo(copy);
                }
            }

            foreach (var (key, path) in loose)
            {
                zip.CreateEntryFromFile(path, key, CompressionLevel.SmallestSize);
            }
        }

        // The loose files go only once the archive is whole, so a process that dies here leaves one or the other intact.
        File.Move(partial, archive, overwrite: true);

        long packed = new FileInfo(archive).Length;
        long size = loose.Sum(file => new FileInfo(file.Path).Length);

        if (ExportRecovery.ExportDirectory(directory) is { } folder)
        {
            Directory.Delete(folder, recursive: true);
        }

        Console.WriteLine($"   {size / 1024:N0} KB of export packed into {packed / 1024:N0} KB");
    }

    private static void Announce(string letter, RunTarget target, IReadOnlyList<Type> selected, string job, string? filter, double soakMinutes, StopSignal stop)
    {
        Console.WriteLine($"Track {letter} — {selected.Count} suite(s), job {job}");
        Console.WriteLine($"  destination  {target.Directory}");
        Console.WriteLine($"  kind         {target.Kind} — {target.Reason}");
        Console.WriteLine($"  filter       {filter ?? "none"}");

        if (letter == "A")
        {
            Console.WriteLine($"  soak         {(soakMinutes > 0 && filter is null ? $"{soakMinutes.ToString("0.#", CultureInfo.InvariantCulture)} min" : "skipped")}");
        }

        Console.WriteLine($"  working tree {(target.Dirty ? "dirty" : "clean")}" + (target.Dirty && target.AllowDirty ? ", allowed by --allow-dirty" : string.Empty));

        if (target.IsBaseline)
        {
            Console.WriteLine($"  measured in  {target.WorkingDirectory}, moved to the destination once every suite has run");
        }

        Console.WriteLine(stop.ReadsControlC
            ? $"  to stop      Ctrl+C, or a file named STOP in {target.WorkingDirectory}: the current suite finishes and what was measured is written. Ctrl+C twice stops at once."
            : $"  to stop      a file named STOP in {target.WorkingDirectory}: the current suite finishes and what was measured is written.");
    }

    private static void Step(string name, Action work)
    {
        Console.WriteLine();
        Console.WriteLine($"── {name}");

        var timer = Stopwatch.StartNew();
        work();

        Console.WriteLine($"   {name} took {timer.Elapsed:hh\\:mm\\:ss}");
    }

    /// <summary>
    /// The suites a run covers. A filter is a comma-separated list, and a suite is selected when any token names it:
    /// a stage — <c>a1</c>, <c>a2</c>, <c>a3</c>, <c>b1</c> — or a glob over the suite's type name.
    /// </summary>
    private static IReadOnlyList<Type> Select(string? filter, (string Stage, Type Suite)[] track)
    {
        if (filter is null)
        {
            return [.. track.Select(entry => entry.Suite)];
        }

        var tokens = filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return
        [
            .. track
                .Where(entry => tokens.Any(token => string.Equals(token, entry.Stage, StringComparison.OrdinalIgnoreCase) || Matches(entry.Suite.Name, token)))
                .Select(entry => entry.Suite),
        ];
    }

    /// <summary>A glob over a suite's type name.</summary>
    internal static bool Matches(string name, string pattern)
    {
        var parts = pattern.Split('*');
        var position = 0;

        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length == 0)
            {
                continue;
            }

            int found = name.IndexOf(parts[index], position, StringComparison.OrdinalIgnoreCase);

            if (found < 0 || (index == 0 && !pattern.StartsWith('*') && found != 0))
            {
                return false;
            }

            position = found + parts[index].Length;
        }

        return pattern.EndsWith('*') || position == name.Length;
    }

    /// <summary>Suites that carry benchmarks but belong to neither track. A run refuses rather than quietly measuring less.</summary>
    private static IReadOnlyList<string> Unregistered() =>
    [
        .. typeof(TrackRunner).Assembly.GetTypes()
            .Where(type => type is { IsPublic: true, IsAbstract: false })
            .Where(type => type.GetMethods().Any(method => method.GetCustomAttribute<BenchmarkAttribute>() is not null))
            .Where(type => !TrackA.Concat(TrackB).Any(entry => entry.Suite == type))
            .Select(type => type.FullName ?? type.Name)
            .Order(StringComparer.Ordinal),
    ];

    private static void Manifest(string directory, RunRecord record, IReadOnlyList<ResultRow> rows)
    {
        var text = new StringBuilder();

        text.Append(CultureInfo.InvariantCulture, $"# {record.Kind} {record.Label}\n\n");
        text.Append(CultureInfo.InvariantCulture, $"- track: {record.Track}\n");
        text.Append(CultureInfo.InvariantCulture, $"- job: {record.Job}\n");
        text.Append(CultureInfo.InvariantCulture, $"- tag: {record.Tag ?? "none"}\n");
        text.Append(CultureInfo.InvariantCulture, $"- git describe: {record.Describe}\n");
        text.Append(CultureInfo.InvariantCulture, $"- revision: {record.Revision}\n");

        if (record.MeasuredAs is not null)
        {
            text.Append(CultureInfo.InvariantCulture,
                $"- measured as: {record.MeasuredAs}, from the revision above; src/ differs from it only in " +
                $"{(record.SourceDifferences is { Count: > 0 } files ? string.Join(", ", files) : "nothing")}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"- started (UTC): {record.StartedUtc}\n");
        text.Append(CultureInfo.InvariantCulture, $"- finished (UTC): {record.FinishedUtc}\n");
        text.Append(CultureInfo.InvariantCulture, $"- elapsed: {Elapsed(record):hh\\:mm\\:ss}\n");
        text.Append(CultureInfo.InvariantCulture, $"- suites run: {record.SuitesRun.Count}\n");
        text.Append(CultureInfo.InvariantCulture, $"- timed cells: {rows.Count}\n");
        text.Append(CultureInfo.InvariantCulture, $"- cells without a number: {rows.Count(row => !row.IsSupported)}\n");
        text.Append(CultureInfo.InvariantCulture, $"- why this kind: {record.Reason}\n\n");
        text.Append("The machine, the runtime, the GC mode, the libraries as loaded and every pinned package are in `environment.json`.\n");

        File.WriteAllText(Path.Combine(directory, "manifest.md"), text.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// The commands that produced this run, naming only what the repository contains, because a reader of this
    /// directory has the repository and nothing else.
    /// </summary>
    private static void Reproduction(string directory, RunRecord record)
    {
        var checkout = record.MeasuredAs is null ? record.Tag ?? record.Revision : record.Revision;
        var run = $"dotnet run --project {Paths.ProjectName} -c Release --";
        var folder = record.IsBaseline ? "Baselines" : "Measurements";
        var tagged = (record.Tag, record.MeasuredAs) switch
        {
            (null, _) => string.Empty,
            (_, null) => $", tag `{record.Tag}`",
            _ => $", filed under tag `{record.MeasuredAs}`, whose `src/` it carries in code",
        };

        var whole = record.IsMarket
            ? "The last line is the whole of it: it writes the manifest, probes every adapter's capabilities, places the " +
              "tiers, verifies every timed pair, runs the comparative suites in order and generates this directory's report."
            : "The last line is the whole of it: it writes the manifest, invokes every benchmark once to check that none " +
              "leaves a scope open, runs the suites in order, measures cold start and sustained load, and generates this " +
              "directory's report and charts.";

        var parts = record.IsMarket
            ? $"{run} --verify --market     # probes, tiers and pairs, no timing\n{run} --manifest             # the environment\n{run} --track B --filter '*Rollback*'"
            : $"{run} --verify              # every benchmark once, no timing\n{run} --manifest            # the environment\n{run} --cold                # cold start\n{run} --soak 5              # sustained load\n{run} --track A --filter a2";

        var text = $"""
            # Reproducing {record.Label}

            Revision `{record.Revision}`{tagged}. Nothing below depends on a file outside this repository.

            ## Machine state

            The timings of this run belong to the machine `environment.json` records. Reproduce them on the same hardware,
            on mains power, with no other interactive workload, and under the same GC mode. Allocation per operation, the
            verification outcomes and, for Track B, the probes and the tiers do not depend on the machine.

            ## Commands, in order

            ```text
            git clone <this repository>
            git checkout {checkout}
            dotnet restore Rewind.slnx
            dotnet build Rewind.slnx --configuration Release --no-restore
            {string.Join(System.Environment.NewLine, record.Commands)}
            ```

            {whole}

            ## Re-running one part alone

            ```text
            {parts}
            {run} --report {folder}/{record.Label}
            ```

            A filter narrows the run to the suites whose name or stage matches it, and a narrowed run is a measurement: it
            lands under `Measurements/` and never under `Baselines/`. The last line regenerates the report and the charts
            from the files already in this directory.

            ## Checking these instructions are complete

            Run them in a container that starts from the vendor image and holds nothing from this machine. It compares the
            verification outcomes and the probes, which are machine-independent, and no timing.

            ```text
            docker run --rm -v "$PWD:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0 bash /src/{Paths.ProjectName}/reproduction/check.sh {checkout} {Paths.ProjectName}/{folder}/{record.Label}
            ```

            """;

        File.WriteAllText(Path.Combine(directory, "reproduction.md"), text, Encoding.UTF8);
    }

    /// <summary>What a measurement did not measure, so a directory holding half the suites cannot be read as a baseline.</summary>
    private static void Scope(string directory, RunRecord record)
    {
        var text = new StringBuilder();

        text.Append(CultureInfo.InvariantCulture, $"# Scope of {record.Label}\n\n");
        text.Append("**This is not a baseline.** ").Append(record.Reason).Append(".\n\n");
        text.Append("A figure in this directory describes only what the suites below measured, on the revision\n");
        text.Append('`').Append(record.Revision).Append("`. Nothing here may be cited about anything else.\n\n");
        text.Append(CultureInfo.InvariantCulture, $"## Measured — {record.SuitesRun.Count} suite(s)\n\n");

        foreach (var suite in record.SuitesRun)
        {
            text.Append("- ").Append(suite).Append('\n');
        }

        if (record.Interrupted)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n## Interrupted — by {record.StoppedBy}\n\n");
            text.Append("The run ended before its last suite. It is a measurement whatever its tag and its job.\n");

            foreach (var suite in record.SuitesUnfinished ?? [])
            {
                text.Append("\n- ").Append(suite).Append(" was cut off in the middle; BenchmarkDotNet exports a suite only when it ends,\n");
                text.Append("  so none of its cells is in this directory.\n");
            }
        }

        text.Append(CultureInfo.InvariantCulture, $"\n## Not measured — {record.SuitesNotRun.Count} suite(s) or step(s)\n\n");

        if (record.SuitesNotRun.Count == 0)
        {
            text.Append("None: every suite of the track ran. The run is still not a baseline, for the reason above.\n");
        }
        else
        {
            foreach (var suite in record.SuitesNotRun)
            {
                text.Append("- ").Append(suite).Append('\n');
            }
        }

        File.WriteAllText(Path.Combine(directory, "scope.md"), text.ToString(), Encoding.UTF8);
    }

    private static string? Value(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static double Number(string[] args, string name, double fallback) =>
        Value(args, name) is { } text && double.TryParse(text, CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback;
}
