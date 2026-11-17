using System.Globalization;
using System.Text;
using BenchmarkDotNet.Reports;

namespace ViShap.Rewind.Benchmarks.Reporting;

/// <summary>
/// One timed cell, as it is written to <c>results.csv</c> and read back by the report, the charts and the
/// comparison.
/// </summary>
/// <remarks>
/// Times are nanoseconds and allocation is bytes per operation, as BenchmarkDotNet measured them. <c>State</c> says
/// whether the cell produced a number: one that did not says so rather than being left blank.
/// </remarks>
internal sealed record ResultRow(
    string Suite,
    string Method,
    string Parameters,
    string Job,
    string State,
    double MeanNs,
    double ErrorNs,
    double StdDevNs,
    double MedianNs,
    double P95Ns,
    double OpsPerSecond,
    double AllocatedBytes,
    double Gen0Per1000,
    double Gen1Per1000,
    double Gen2Per1000,
    int Iterations)
{
    internal const string Supported = "Supported";

    private const string Header =
        "suite,method,parameters,job,state,mean_ns,error_ns,stddev_ns,median_ns,p95_ns," +
        "ops_per_second,allocated_bytes,gen0_per_1000,gen1_per_1000,gen2_per_1000,iterations";

    /// <summary>
    /// The margin of error in nanoseconds: the half-width of the 99.9 % interval, what BenchmarkDotNet prints as
    /// <c>Error</c>. <c>ErrorNs</c> is the standard error the export records.
    /// </summary>
    internal double MarginNs => Confidence.Margin(ErrorNs, Iterations);

    /// <summary>The relative margin of error, printed beside every cell.</summary>
    internal double RelativeErrorPercent => MeanNs > 0 ? MarginNs / MeanNs * 100 : 0;

    /// <summary>The short name of the suite, without its namespace.</summary>
    internal string SuiteName => Suite[(Suite.LastIndexOf('.') + 1)..];

    internal bool IsSupported => State == Supported;

    /// <summary>The value of one parameter of this cell, or <see langword="null"/> when the suite has no such parameter.</summary>
    internal string? Parameter(string name)
    {
        foreach (var pair in Parameters.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int split = pair.IndexOf('=', StringComparison.Ordinal);

            if (split > 0 && pair.AsSpan(0, split).Trim().SequenceEqual(name))
            {
                return pair[(split + 1)..].Trim();
            }
        }

        return null;
    }

    /// <summary>The parameters other than <paramref name="names"/>, as the cell writes them.</summary>
    internal string ParametersWithout(params string[] names) =>
        string.Join("; ", Parameters
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(pair => !names.Any(name => pair.StartsWith(name + "=", StringComparison.Ordinal))));

    internal static IReadOnlyList<ResultRow> From(IEnumerable<Summary> summaries)
    {
        var rows = new List<ResultRow>();

        foreach (var summary in summaries)
        {
            foreach (var report in summary.Reports)
            {
                var descriptor = report.BenchmarkCase.Descriptor;
                var statistics = report.ResultStatistics;
                var gc = report.GcStats;
                double operations = gc.TotalOperations > 0 ? gc.TotalOperations : 1;

                rows.Add(new ResultRow(
                    Suite: descriptor.Type.FullName ?? descriptor.Type.Name,
                    Method: descriptor.WorkloadMethodDisplayInfo,
                    Parameters: Describe(report.BenchmarkCase.Parameters),
                    Job: report.BenchmarkCase.Job.ResolvedId,
                    State: report.Success && statistics is not null ? Supported : "Failed",
                    MeanNs: statistics?.Mean ?? 0,
                    ErrorNs: statistics?.StandardError ?? 0,
                    StdDevNs: statistics?.StandardDeviation ?? 0,
                    MedianNs: statistics?.Median ?? 0,
                    P95Ns: statistics?.Percentiles.P95 ?? 0,
                    OpsPerSecond: statistics is { Mean: > 0 } ? 1_000_000_000d / statistics.Mean : 0,
                    AllocatedBytes: gc.GetBytesAllocatedPerOperation(report.BenchmarkCase) ?? 0,
                    Gen0Per1000: gc.Gen0Collections / operations * 1000,
                    Gen1Per1000: gc.Gen1Collections / operations * 1000,
                    Gen2Per1000: gc.Gen2Collections / operations * 1000,
                    Iterations: statistics?.N ?? 0));
            }
        }

        return rows;
    }

    internal static void Write(IReadOnlyList<ResultRow> rows, string path)
    {
        var csv = new StringBuilder(Header).Append('\n');

        foreach (var row in rows)
        {
            csv.Append(CultureInfo.InvariantCulture,
                $"{Csv.Quote(row.Suite)},{Csv.Quote(row.Method)},{Csv.Quote(row.Parameters)},{Csv.Quote(row.Job)},{row.State},");
            csv.Append(CultureInfo.InvariantCulture,
                $"{N(row.MeanNs)},{N(row.ErrorNs)},{N(row.StdDevNs)},{N(row.MedianNs)},{N(row.P95Ns)},");
            csv.Append(CultureInfo.InvariantCulture,
                $"{N(row.OpsPerSecond)},{N(row.AllocatedBytes)},{N(row.Gen0Per1000)},");
            csv.Append(CultureInfo.InvariantCulture,
                $"{N(row.Gen1Per1000)},{N(row.Gen2Per1000)},{row.Iterations}\n");
        }

        File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Reads the table back. The report is a view over this file rather than over the run, so it can be regenerated
    /// from a committed baseline long after the process that measured it exited.
    /// </summary>
    internal static IReadOnlyList<ResultRow> Read(string path) =>
    [
        .. Csv.Read(path).Select(cells => new ResultRow(
            cells["suite"], cells["method"], cells["parameters"], cells["job"], cells["state"],
            D(cells["mean_ns"]), D(cells["error_ns"]), D(cells["stddev_ns"]), D(cells["median_ns"]), D(cells["p95_ns"]),
            D(cells["ops_per_second"]), D(cells["allocated_bytes"]), D(cells["gen0_per_1000"]),
            D(cells["gen1_per_1000"]), D(cells["gen2_per_1000"]),
            int.Parse(cells["iterations"], CultureInfo.InvariantCulture))),
    ];

    /// <summary>The allocation of every cell, written without the timings beside it.</summary>
    internal static void WriteMemory(IReadOnlyList<ResultRow> rows, string path)
    {
        var csv = new StringBuilder("suite,method,parameters,allocated_bytes,gen0_per_1000,gen1_per_1000,gen2_per_1000\n");

        foreach (var row in rows)
        {
            csv.Append(CultureInfo.InvariantCulture,
                $"{Csv.Quote(row.Suite)},{Csv.Quote(row.Method)},{Csv.Quote(row.Parameters)},{N(row.AllocatedBytes)},");
            csv.Append(CultureInfo.InvariantCulture,
                $"{N(row.Gen0Per1000)},{N(row.Gen1Per1000)},{N(row.Gen2Per1000)}\n");
        }

        File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// The parameters of a case as <c>name=value</c> pairs separated by semicolons, written by this harness rather
    /// than taken from a BenchmarkDotNet display string, so a chart can read one back.
    /// </summary>
    private static string Describe(BenchmarkDotNet.Parameters.ParameterInstances parameters) =>
        string.Join("; ", parameters.Items.Select(item => $"{item.Name}={item.Value}"));

    private static string N(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    private static double D(string value) => double.Parse(value, CultureInfo.InvariantCulture);
}
