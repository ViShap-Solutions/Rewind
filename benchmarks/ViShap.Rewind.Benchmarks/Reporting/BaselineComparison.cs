using System.Globalization;
using System.Text;

namespace ViShap.Rewind.Benchmarks.Reporting;

/// <summary>
/// The delta of a run against a baseline, cell by cell, with margins of error. Both sides are read from their raw
/// files and nothing is recomputed. A timed cell is faster or slower only when its interval — the mean ± its 99.9 %
/// margin — does not overlap the baseline's; otherwise it is within error, whatever its ratio says.
/// </summary>
/// <remarks>
/// Written beside the run as <c>comparison-&lt;baseline&gt;.md</c> for reading, <c>.csv</c> for tools and
/// <c>.svg</c> for the eye. A cell present on one side only is listed, never dropped: a renamed benchmark shows up as
/// one missing cell and one new cell. Allocation is compared exactly: it is deterministic, so any change is real.
/// </remarks>
internal static class BaselineComparison
{
    private const string NotMeasured = "suite not measured";

    internal static int Run(string baselineDirectory, string runDirectory, string? output)
    {
        foreach (var directory in new[] { baselineDirectory, runDirectory })
        {
            if (!File.Exists(Path.Combine(directory, "results.csv")))
            {
                Console.Error.WriteLine($"No results.csv in '{directory}'.");
                return 1;
            }
        }

        string baselineName = Name(baselineDirectory);
        string runName = Name(runDirectory);

        var rows = Compare(baselineDirectory, runDirectory);
        var cold = CompareColdStart(baselineDirectory, runDirectory);

        var markdown = new StringBuilder();
        markdown.AppendLine(CultureInfo.InvariantCulture, $"# {runName} against {baselineName}");
        markdown.AppendLine();
        markdown.AppendLine(
            "A timed cell is `faster` or `slower` only when its interval, mean ± its 99.9 % margin of error, does not " +
            "overlap the baseline's; otherwise it is `within error`. Ratio is run ÷ baseline. Allocation is compared " +
            "exactly, since it does not depend on the machine's load.");
        markdown.AppendLine();
        markdown.AppendLine("| Verdict | Cells |");
        markdown.AppendLine("|---|---|");

        foreach (var group in rows.Where(row => row.Verdict != NotMeasured).GroupBy(row => row.Verdict).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            markdown.AppendLine(CultureInfo.InvariantCulture, $"| {group.Key} | {group.Count()} |");
        }

        int allocationChanged = rows.Count(row => row.AllocationChanged);
        markdown.AppendLine();
        markdown.AppendLine(CultureInfo.InvariantCulture, $"Allocation changed in {allocationChanged} cell(s).");

        Section(markdown, "results.csv", rows);
        Section(markdown, "cold-start.csv", cold);

        string markdownPath = output ?? Path.Combine(runDirectory, $"comparison-{baselineName.Replace('/', '-')}.md");
        File.WriteAllText(markdownPath, markdown.ToString(), Encoding.UTF8);
        Csv.Write(
            Path.ChangeExtension(markdownPath, ".csv"),
            ["file", "cell", "baseline", "run", "ratio", "baseline_allocated_bytes", "run_allocated_bytes", "verdict"],
            rows.Concat(cold).Select(row => (IReadOnlyList<string>)
                [row.File, row.Cell, row.Baseline, row.Run, row.Ratio, row.BaselineAllocated, row.RunAllocated, row.Verdict]));
        File.WriteAllText(Path.ChangeExtension(markdownPath, ".svg"), Chart(rows, runName, baselineName));

        foreach (var group in rows.GroupBy(row => row.Verdict).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"{group.Count(),5} {group.Key}");
        }

        Console.WriteLine($"Allocation changed in {allocationChanged} cell(s).");
        Console.WriteLine($"Written to {markdownPath}");
        return 0;
    }

    /// <summary>One compared cell, as the csv output carries it.</summary>
    private sealed record Row(
        string File,
        string Cell,
        string Baseline,
        string Run,
        string Ratio,
        string BaselineAllocated,
        string RunAllocated,
        string Verdict,
        double RatioValue = double.NaN)
    {
        internal bool AllocationChanged => BaselineAllocated.Length > 0 && RunAllocated.Length > 0 && BaselineAllocated != RunAllocated;
    }

    private static List<Row> Compare(string baselineDirectory, string runDirectory)
    {
        static string Key(ResultRow row) => $"{row.SuiteName} | {row.Method} | {row.Parameters}";

        var baseline = ResultRow.Read(Path.Combine(baselineDirectory, "results.csv")).ToDictionary(Key, StringComparer.Ordinal);
        var run = ResultRow.Read(Path.Combine(runDirectory, "results.csv")).ToDictionary(Key, StringComparer.Ordinal);
        var measured = run.Values.Select(row => row.SuiteName).ToHashSet(StringComparer.Ordinal);
        var rows = new List<Row>();

        // A run narrowed by a filter holds some suites and not others; a suite it did not run is reported once.
        foreach (var suite in baseline.Values.Select(row => row.SuiteName).Distinct().Where(suite => !measured.Contains(suite)).Order(StringComparer.Ordinal))
        {
            rows.Add(new Row("results.csv", suite, "", "", "", "", "", NotMeasured));
        }

        foreach (var key in baseline.Keys.Union(run.Keys).Where(key => measured.Contains(key[..key.IndexOf(" | ", StringComparison.Ordinal)])).Order(StringComparer.Ordinal))
        {
            baseline.TryGetValue(key, out var before);
            run.TryGetValue(key, out var after);

            if (before is null || after is null)
            {
                rows.Add(new Row(
                    "results.csv", key,
                    before is null ? "" : Format.Interval(before),
                    after is null ? "" : Format.Interval(after),
                    "",
                    before is null ? "" : Bytes(before.AllocatedBytes),
                    after is null ? "" : Bytes(after.AllocatedBytes),
                    before is null ? "run only" : "baseline only"));
                continue;
            }

            string verdict = !before.IsSupported || !after.IsSupported
                ? $"state {before.State} → {after.State}"
                : Verdict(before.MeanNs - before.MarginNs, before.MeanNs + before.MarginNs, after.MeanNs - after.MarginNs, after.MeanNs + after.MarginNs);

            if (before.Job != after.Job)
            {
                verdict += $" (job {before.Job} → {after.Job})";
            }

            double ratio = before.MeanNs > 0 ? after.MeanNs / before.MeanNs : double.NaN;

            rows.Add(new Row(
                "results.csv", key, Format.Interval(before), Format.Interval(after),
                double.IsNaN(ratio) ? "" : ratio.ToString("F3", CultureInfo.InvariantCulture),
                Bytes(before.AllocatedBytes), Bytes(after.AllocatedBytes), verdict, ratio));
        }

        return rows;
    }

    private static List<Row> CompareColdStart(string baselineDirectory, string runDirectory)
    {
        static string Key(IReadOnlyDictionary<string, string> cell) => cell["scenario"];

        var baseline = Csv.Read(Path.Combine(baselineDirectory, "cold-start.csv")).ToDictionary(Key, StringComparer.Ordinal);
        var run = Csv.Read(Path.Combine(runDirectory, "cold-start.csv")).ToDictionary(Key, StringComparer.Ordinal);
        var rows = new List<Row>();

        static string Operation(IReadOnlyDictionary<string, string> cell) =>
            $"{cell["first_operation_us_median"]} µs [{cell["first_operation_us_min"]}, {cell["first_operation_us_p95"]}]";

        foreach (var key in baseline.Keys.Union(run.Keys).Order(StringComparer.Ordinal))
        {
            baseline.TryGetValue(key, out var before);
            run.TryGetValue(key, out var after);

            if (before is null || after is null)
            {
                rows.Add(new Row("cold-start.csv", key, before is null ? "" : Operation(before), after is null ? "" : Operation(after), "", "", "",
                    before is null ? "run only" : "baseline only"));
                continue;
            }

            // A distribution over launches is compared by its spread from minimum to p95.
            double ratio = Number(after["first_operation_us_median"]) / Number(before["first_operation_us_median"]);

            rows.Add(new Row(
                "cold-start.csv", key, Operation(before), Operation(after), ratio.ToString("F3", CultureInfo.InvariantCulture),
                before["allocated_bytes_median"], after["allocated_bytes_median"],
                Verdict(
                    Number(before["first_operation_us_min"]), Number(before["first_operation_us_p95"]),
                    Number(after["first_operation_us_min"]), Number(after["first_operation_us_p95"]))));
        }

        return rows;
    }

    private static string Verdict(double baselineLow, double baselineHigh, double runLow, double runHigh) =>
        runHigh < baselineLow ? "faster" : runLow > baselineHigh ? "slower" : "within error";

    private static void Section(StringBuilder markdown, string file, IReadOnlyList<Row> section)
    {
        markdown.AppendLine();
        markdown.AppendLine(CultureInfo.InvariantCulture, $"## {file}");
        markdown.AppendLine();

        if (section.Count == 0)
        {
            markdown.AppendLine("Neither run holds this file.");
            return;
        }

        var unmeasured = section.Where(row => row.Verdict == NotMeasured).Select(row => row.Cell).ToArray();

        if (unmeasured.Length > 0)
        {
            markdown.AppendLine(CultureInfo.InvariantCulture, $"Suites of the baseline this run did not measure: {string.Join(", ", unmeasured)}.");
            markdown.AppendLine();
        }

        markdown.AppendLine("| Cell | Baseline | Run | Ratio | Baseline alloc | Run alloc | Verdict |");
        markdown.AppendLine("|---|---|---|---|---|---|---|");

        foreach (var row in section.Where(row => row.Verdict != NotMeasured))
        {
            var allocation = row.AllocationChanged ? $"**{row.RunAllocated}**" : row.RunAllocated;
            markdown.AppendLine(CultureInfo.InvariantCulture,
                $"| {row.Cell.Replace("|", "\\|", StringComparison.Ordinal)} | {row.Baseline} | {row.Run} | {row.Ratio} | {row.BaselineAllocated} | {allocation} | {row.Verdict} |");
        }
    }

    /// <summary>Every compared cell's ratio as a bar, coloured by its verdict, with 1.0 marked.</summary>
    private static string Chart(IReadOnlyList<Row> rows, string runName, string baselineName)
    {
        var chart = new BarChart($"{runName} ÷ {baselineName}", "ratio of means, 1.0 is unchanged", lowerIsBetter: true, labelWidth: 420);

        foreach (var row in rows.Where(row => !double.IsNaN(row.RatioValue)))
        {
            int colour = row.Verdict.StartsWith("faster", StringComparison.Ordinal) ? 2
                : row.Verdict.StartsWith("slower", StringComparison.Ordinal) ? 4
                : 7;
            chart.Add(row.Cell, row.RatioValue, colour, group: row.Cell[..row.Cell.IndexOf(" | ", StringComparison.Ordinal)]);
        }

        return chart.Render();
    }

    private static string Bytes(double value) => value.ToString("F0", CultureInfo.InvariantCulture);

    private static double Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.NaN;

    private static string Name(string directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var name = Path.GetFileName(full);

        // A Track B baseline lives in <tag>/market; its name is both.
        return name == "market" ? $"{Path.GetFileName(Path.GetDirectoryName(full))}/market" : name;
    }
}
