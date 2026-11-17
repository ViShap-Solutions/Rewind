using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace ViShap.Rewind.Benchmarks.Environment;

/// <summary>
/// Every package the benchmarks reference, with the version pinned for it and the latest stable version nuget.org
/// lists on the day of the run, so a comparison against an outdated competitor is visible in the run that made it.
/// Best-effort: without the network the field says so and the run goes on.
/// </summary>
internal static class PackageCurrency
{
    private const string Index = "https://api.nuget.org/v3-flatcontainer/{0}/index.json";

    /// <summary>
    /// One package: the version pinned, the newest stable one of the same major version, and the newest stable one
    /// overall.
    /// </summary>
    internal sealed record Entry(string Pinned, string? Latest, string? LatestOverall, bool Current);

    /// <summary>
    /// The packages the project references, each with the version the central package files pin: the benchmarks'
    /// own <c>Directory.Packages.props</c> and the repository's.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> Pinned()
    {
        var pinned = new SortedDictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var referenced = XDocument.Load(Paths.ProjectFilePath)
                .Descendants("PackageReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var benchmarks = Path.GetDirectoryName(Paths.ProjectDirectory)!;

            foreach (var file in new[]
                     {
                         Path.Combine(benchmarks, "Directory.Packages.props"),
                         Path.Combine(Path.GetDirectoryName(benchmarks)!, "Directory.Packages.props"),
                     })
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                foreach (var version in XDocument.Load(file).Descendants("PackageVersion"))
                {
                    var id = version.Attribute("Include")?.Value;
                    var value = version.Attribute("Version")?.Value;

                    if (id is not null && value is not null && referenced.Contains(id))
                    {
                        pinned.TryAdd(id, value);
                    }
                }
            }
        }
        catch (Exception)
        {
            // A manifest without package versions is still a manifest; the gap is visible in it.
        }

        return pinned;
    }

    internal static IReadOnlyDictionary<string, Entry> Check(IReadOnlyDictionary<string, string> pinned)
    {
        var result = new SortedDictionary<string, Entry>(StringComparer.Ordinal);

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            foreach (var (id, version) in pinned)
            {
                var listed = Stable(http, id);
                var major = version.Split('.')[0] + ".";
                var latest = listed.LastOrDefault(candidate => candidate.StartsWith(major, StringComparison.Ordinal));
                result[id] = new Entry(version, latest, listed.LastOrDefault(), latest is null || latest == version);
            }
        }
        catch (Exception)
        {
            // No network, or nuget.org unreachable: the entries read so far stand, the rest are absent.
        }

        return result;
    }

    /// <summary>The packages that have a newer stable version of the same major than the one pinned.</summary>
    internal static IReadOnlyList<string> Outdated(IReadOnlyDictionary<string, Entry> entries) =>
    [
        .. entries
            .Where(entry => !entry.Value.Current)
            .Select(entry => $"{entry.Key} {entry.Value.Pinned} → {entry.Value.Latest}"),
    ];

    private static List<string> Stable(HttpClient http, string id)
    {
        try
        {
            using var document = JsonDocument.Parse(
                http.GetStringAsync(string.Format(CultureInfo.InvariantCulture, Index, id.ToLowerInvariant()))
                    .GetAwaiter().GetResult());

            return
            [
                .. document.RootElement.GetProperty("versions").EnumerateArray()
                    .Select(version => version.GetString() ?? string.Empty)
                    .Where(version => version.Length > 0 && !version.Contains('-', StringComparison.Ordinal)),
            ];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
