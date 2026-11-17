using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace ViShap.Rewind.Benchmarks.Reporting;

/// <summary>
/// Reads the cells of a run back from BenchmarkDotNet's own exports, which survive whatever ended the process:
/// <c>results/*-report-full.json</c>, or the same files packed into <c>results.zip</c>.
/// </summary>
/// <remarks>
/// A cell is keyed exactly as <see cref="ResultRow.From"/> keys it. The parameters are taken from the benchmark's
/// <c>FullName</c>; the job is the id that opens the job part of <c>DisplayInfo</c>. A suite BenchmarkDotNet did not
/// finish has no export, so its cells are not here.
/// </remarks>
internal static class ExportRecovery
{
    private const string ExportSuffix = "-report-full.json";

    /// <summary>One suite's export: the suite's short name, its cells, and when it was written.</summary>
    internal sealed record SuiteExport(string Suite, IReadOnlyList<ResultRow> Rows, DateTime WrittenUtc);

    /// <summary>Every suite export the directory holds, loose or archived, in <paramref name="order"/>.</summary>
    internal static IReadOnlyList<SuiteExport> Read(string directory, IReadOnlyList<string> order)
    {
        var exports = new Dictionary<string, SuiteExport>(StringComparer.Ordinal);

        foreach (var (key, path) in LooseFiles(directory).Where(file => file.Key.EndsWith(ExportSuffix, StringComparison.Ordinal)))
        {
            exports[key] = Parse(File.ReadAllText(path, Encoding.UTF8), key, File.GetLastWriteTimeUtc(path));
        }

        var archive = Path.Combine(directory, "results.zip");

        if (File.Exists(archive))
        {
            try
            {
                using var zip = ZipFile.OpenRead(archive);

                foreach (var entry in zip.Entries.Where(entry => entry.FullName.EndsWith(ExportSuffix, StringComparison.Ordinal)))
                {
                    if (exports.ContainsKey(entry.FullName))
                    {
                        continue;
                    }

                    using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                    exports[entry.FullName] = Parse(reader.ReadToEnd(), entry.FullName, entry.LastWriteTime.UtcDateTime);
                }
            }
            catch (InvalidDataException)
            {
                // An archive cut off while it was written is unreadable; the loose files it was made from are deleted
                // only after it is complete, so they are still there.
            }
        }

        return
        [
            .. exports.Values
                .OrderBy(export => order.ToList().IndexOf(export.Suite) is >= 0 and var index ? index : int.MaxValue)
                .ThenBy(export => export.Suite, StringComparer.Ordinal),
        ];
    }

    /// <summary>The directory BenchmarkDotNet exported into, when a run directory has one.</summary>
    internal static string? ExportDirectory(string directory) =>
        Path.Combine(directory, "results") is var path && Directory.Exists(path) ? path : null;

    /// <summary>Every file BenchmarkDotNet exported into a run directory, keyed by its name inside <c>results.zip</c>.</summary>
    internal static IEnumerable<(string Key, string Path)> LooseFiles(string directory) =>
        ExportDirectory(directory) is { } folder
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(path => (Path.GetRelativePath(folder, path).Replace('\\', '/'), path))
            : [];

    private static SuiteExport Parse(string json, string key, DateTime writtenUtc)
    {
        using var document = JsonDocument.Parse(json);
        var rows = new List<ResultRow>();
        string? suite = null;

        foreach (var benchmark in document.RootElement.GetProperty("Benchmarks").EnumerateArray())
        {
            var type = benchmark.GetProperty("Type").GetString()!;
            var title = benchmark.GetProperty("MethodTitle").GetString()!;
            var ns = benchmark.GetProperty("Namespace").GetString();
            suite ??= type;

            var statistics = Object(benchmark, "Statistics");
            var memory = Object(benchmark, "Memory");
            double operations = memory is { } gc && Number(gc, "TotalOperations") is > 0 and var total ? total : 1;
            double mean = statistics is { } s ? Number(s, "Mean") : 0;

            rows.Add(new ResultRow(
                Suite: string.IsNullOrEmpty(ns) ? type : $"{ns}.{type}",
                Method: title,
                Parameters: Parameters(benchmark.GetProperty("FullName").GetString()!),
                Job: Job(benchmark.GetProperty("DisplayInfo").GetString()!, $"{type}.{title}: "),
                State: statistics is null ? "Failed" : ResultRow.Supported,
                MeanNs: mean,
                ErrorNs: statistics is { } e ? Number(e, "StandardError") : 0,
                StdDevNs: statistics is { } d ? Number(d, "StandardDeviation") : 0,
                MedianNs: statistics is { } m ? Number(m, "Median") : 0,
                P95Ns: statistics is { } p && Object(p, "Percentiles") is { } percentiles ? Number(percentiles, "P95") : 0,
                OpsPerSecond: mean > 0 ? 1_000_000_000d / mean : 0,
                AllocatedBytes: memory is { } a ? Number(a, "BytesAllocatedPerOperation") : 0,
                Gen0Per1000: memory is { } g0 ? Number(g0, "Gen0Collections") / operations * 1000 : 0,
                Gen1Per1000: memory is { } g1 ? Number(g1, "Gen1Collections") / operations * 1000 : 0,
                Gen2Per1000: memory is { } g2 ? Number(g2, "Gen2Collections") / operations * 1000 : 0,
                Iterations: statistics is { } n ? (int)Number(n, "N") : 0));
        }

        suite ??= Path.GetFileName(key)[..^ExportSuffix.Length].Split('.')[^1];

        return new SuiteExport(suite, rows, writtenUtc);
    }

    private static JsonElement? Object(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static double Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    private static string Job(string displayInfo, string prefix)
    {
        var rest = displayInfo.StartsWith(prefix, StringComparison.Ordinal) ? displayInfo[prefix.Length..] : displayInfo;
        int end = rest.IndexOfAny(['(', '[', ' ']);
        return end < 0 ? rest : rest[..end];
    }

    /// <summary>
    /// <c>Namespace.Type.Method(Name: value, Name: value)</c> as <see cref="ResultRow.From"/> writes the same
    /// parameters: <c>Name=value; Name=value</c>. A comma inside quotes or brackets belongs to the value.
    /// </summary>
    internal static string Parameters(string fullName)
    {
        int open = fullName.IndexOf('(', StringComparison.Ordinal);

        if (open < 0 || !fullName.EndsWith(')'))
        {
            return string.Empty;
        }

        var pairs = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        var quoted = false;

        foreach (char character in fullName.AsSpan(open + 1, fullName.Length - open - 2))
        {
            if (quoted)
            {
                quoted = character != '"';
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character is '(' or '[' or '{')
            {
                depth++;
            }
            else if (character is ')' or ']' or '}')
            {
                depth--;
            }
            else if (character == ',' && depth == 0)
            {
                pairs.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        pairs.Add(current.ToString());

        return string.Join("; ", pairs.Select(pair =>
        {
            var text = pair.Trim().Trim('"');
            int colon = text.IndexOf(": ", StringComparison.Ordinal);
            return colon < 0 ? text : $"{text[..colon]}={text[(colon + 2)..].Trim('"')}";
        }));
    }
}
