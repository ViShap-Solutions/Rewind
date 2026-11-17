using System.Globalization;
using ViShap.Rewind.Benchmarks.Suites;

namespace ViShap.Rewind.Benchmarks.Reporting;

/// <summary>One chart produced by a run, as the report lists it.</summary>
internal sealed record Chart(string File, string Title, string Note);

/// <summary>
/// The charts of a Track A run, generated from the raw files of the run and from nothing else. A chart whose suite did
/// not run is not drawn at all rather than drawn empty, so a measurement produces fewer charts and never a misleading one.
/// </summary>
internal static class Charts
{
    internal static IReadOnlyList<Chart> Write(string directory, IReadOnlyList<ResultRow> results)
    {
        Directory.CreateDirectory(directory);

        var charts = new List<Chart>();

        void Emit(string file, string title, string note, string svg)
        {
            File.WriteAllText(Path.Combine(directory, file), svg);
            charts.Add(new Chart(file, title, note));
        }

        // The mechanism record: one bar chart per workload, every cell with its interval.
        foreach (var (suite, file, title, groupBy) in new[]
        {
            ("NoScopeWriteBenchmarks", "w1-no-scope-write.svg", "W1 — a write with no scope", ""),
            ("ScopeWriteBenchmarks", "w2-w3-scope-writes.svg", "W2, W3 — a scope with N writes, commit and rollback", "Writes,Cells"),
            ("NestingBenchmarks", "w4-nesting.svg", "W4 — nesting depth D", "Depth"),
            ("RunFormBenchmarks", "w5-run-forms.svg", "W5 — the forms of opening a scope", ""),
            ("EffectBenchmarks", "w6-effects.svg", "W6 — K effects", "Effects"),
            ("CollectionBenchmarks", "w7-collections.svg", "W7 — M collection mutations", "Mutations"),
            ("ClearRollbackBenchmarks", "w7-clear-rollback.svg", "W7 — the rollback of Clear against the size cleared", "Size"),
            ("PropertyBenchmarks", "w8-properties.svg", "W8 — generated property, cell, plain property", ""),
            ("AsyncScopeBenchmarks", "w9-async-scope.svg", "W9 — an asynchronous scope against Run", ""),
            ("CellWriteBenchmarks", "w9-w10-cell-write.svg", "W9, W10 — a tracked write, default job", "Writes"),
            ("NotificationBenchmarks", "w15-notifications.svg", "W15 — notifications with a subscriber", ""),
        })
        {
            var rows = Rows(results, suite);

            if (rows.Count == 0)
            {
                continue;
            }

            var chart = new BarChart(title, "nanoseconds per operation", lowerIsBetter: true);
            var keys = groupBy.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (var row in rows)
            {
                var group = keys.Length == 0 ? string.Empty : string.Join(", ", keys.Select(key => $"{key} = {row.Parameter(key)}"));
                var label = keys.Length == 0 ? Label(row) : row.Method;
                chart.Add(label, row.MeanNs, Svg.Colour(row.Method), row.MarginNs, group, floor: IsFloor(row));
            }

            Emit(file, title, "Hatched bars are the plain-code floor of the same work with no rollback.", chart.Render());
        }

        // Allocation across the mechanism record, which does not depend on the machine.
        var allocating = results.Where(row => row.IsSupported && row.AllocatedBytes > 0 && Stage(row) == "a1").ToList();

        if (allocating.Count > 0)
        {
            var chart = new BarChart("Allocation per operation — the mechanism record", "bytes per operation", lowerIsBetter: true, labelWidth: 340);

            foreach (var row in allocating)
            {
                chart.Add(Label(row), row.AllocatedBytes, Svg.Colour(row.Method), group: row.SuiteName);
            }

            Emit("allocation.svg", "Allocation per operation", "Deterministic: the same bytes on any machine under the same runtime.", chart.Render());
        }

        // The scaling curves: cost against a parameter that multiplies.
        foreach (var (suite, parameter, xLabel, file, title, logY) in new[]
        {
            ("ScopeSizeScalingBenchmarks", "Writes", "writes in the scope", "w11-scope-size.svg", "W11 — scope size", true),
            ("DepthScalingBenchmarks", "Depth", "nesting depth", "w12-depth.svg", "W12 — nesting depth", true),
            ("ClearScalingBenchmarks", "Size", "items cleared", "w13-clear.svg", "W13 — rollback of Clear against size", false),
        })
        {
            var rows = Rows(results, suite).Where(row => row.Parameter(parameter) is not null).ToList();

            if (rows.Count == 0)
            {
                continue;
            }

            var time = new LineChart($"{title} — time per operation", xLabel, "nanoseconds per operation", logarithmicY: logY);
            var allocation = new LineChart($"{title} — allocation per operation", xLabel, "bytes per operation", logarithmicY: logY);

            foreach (var series in rows.GroupBy(row => row.Method))
            {
                time.Add(series.Key, series.Select(row => (X(row.Parameter(parameter)), row.MeanNs)));
                allocation.Add(series.Key, series.Select(row => (X(row.Parameter(parameter)), Math.Max(row.AllocatedBytes, 1))));
            }

            Emit(file, $"{title}, time", logY ? "Logarithmic axes: a slope of one is cost in proportion; a flat line is constant cost." : "A flat line is cost independent of size.", time.Render());
            Emit(file.Replace(".svg", "-allocation.svg", StringComparison.Ordinal), $"{title}, allocation", "Deterministic on any machine.", allocation.Render());

            if (suite == "ScopeSizeScalingBenchmarks")
            {
                var perWrite = new LineChart("W11 — cost per write against scope size", xLabel, "nanoseconds per write");

                foreach (var series in rows.GroupBy(row => row.Method))
                {
                    perWrite.Add(series.Key, series.Select(row => (X(row.Parameter(parameter)), row.MeanNs / X(row.Parameter(parameter)))));
                }

                Emit("w11-per-write.svg", "W11 — cost per write", "The fixed cost of a scope spread over its writes; the curve settles at the cost of one recorded write.", perWrite.Render());
            }
        }

        // Independent scopes on many threads: aggregate throughput against threads, beside perfect scaling.
        var concurrency = Rows(results, "ConcurrencyBenchmarks").Where(row => row.Parameter("Threads") is not null).ToList();

        if (concurrency.Count > 0)
        {
            var chart = new LineChart("W14 — scopes per second on T threads", "threads", "scopes per second, all threads", logarithmicX: false);

            foreach (var series in concurrency.GroupBy(row => row.Method))
            {
                chart.Add(series.Key, series.Select(row => (X(row.Parameter("Threads")), Throughput(row))));

                var single = series.FirstOrDefault(row => row.Parameter("Threads") == "1");

                if (single is not null)
                {
                    chart.Add($"{series.Key} × T (perfect)", series.Select(row => (X(row.Parameter("Threads")), Throughput(single) * X(row.Parameter("Threads")))));
                }
            }

            Emit("w14-concurrency.svg", "W14 — concurrency", "Higher is better. The gap to the perfect line is what the threads cost one another.", chart.Render());
        }

        return charts;
    }

    /// <summary>Cold start: the median first operation of each scenario, over process launches.</summary>
    internal static Chart? ColdStart(string directory, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var chart = new BarChart("Cold start — the first operation in a fresh process", "microseconds, median over launches", lowerIsBetter: true);

        foreach (var row in rows)
        {
            chart.Add(row["scenario"], Number(row["first_operation_us_median"]), Svg.Colour(row["scenario"]), floor: row["scenario"] == "empty");
        }

        File.WriteAllText(Path.Combine(directory, "cold-start.svg"), chart.Render());
        return new Chart("cold-start.svg", "Cold start", "One process per measurement; the operation alone, process start-up excluded.");
    }

    /// <summary>Sustained load: throughput and the managed heap over the duration.</summary>
    internal static IReadOnlyList<Chart> Soak(string directory, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var throughput = new LineChart("Sustained load — throughput", "seconds", "scopes per second", logarithmicX: false);
        throughput.Add("mixed workload", rows.Select(row => (Number(row["elapsed_s"]), Number(row["ops_per_second"]))));

        var heap = new LineChart("Sustained load — managed heap", "seconds", "megabytes", logarithmicX: false);
        heap.Add("managed heap", rows.Select(row => (Number(row["elapsed_s"]), Number(row["managed_heap_bytes"]) / 1_048_576)));

        File.WriteAllText(Path.Combine(directory, "soak-throughput.svg"), throughput.Render());
        File.WriteAllText(Path.Combine(directory, "soak-heap.svg"), heap.Render());

        return
        [
            new Chart("soak-throughput.svg", "Sustained load, throughput", "A flat line is a steady state."),
            new Chart("soak-heap.svg", "Sustained load, managed heap", "A heap that climbs from sample to sample is state kept that should not be."),
        ];
    }

    /// <summary>The aggregate scopes per second of a concurrency cell.</summary>
    internal static double Throughput(ResultRow row) =>
        row.MeanNs > 0 ? X(row.Parameter("Threads")) * ConcurrencyBenchmarks.ScopesPerThread / (row.MeanNs / 1_000_000_000) : 0;

    /// <summary>Whether a cell is the plain-code floor of its suite.</summary>
    internal static bool IsFloor(ResultRow row) => row.Method.StartsWith("Plain", StringComparison.Ordinal);

    private static string Stage(ResultRow row) => row.SuiteName switch
    {
        "ScopeSizeScalingBenchmarks" or "DepthScalingBenchmarks" or "ClearScalingBenchmarks" => "a2",
        "ConcurrencyBenchmarks" => "a3",
        _ => "a1",
    };

    private static List<ResultRow> Rows(IReadOnlyList<ResultRow> results, string suite) =>
        [.. results.Where(row => row.SuiteName == suite && row.IsSupported)];

    private static string Label(ResultRow row) => string.IsNullOrEmpty(row.Parameters) ? row.Method : $"{row.Method} · {row.Parameters}";

    private static double X(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? Math.Max(parsed, 1) : 1;

    private static double Number(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0;
}
