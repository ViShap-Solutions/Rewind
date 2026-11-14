using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ViShap.Rewind.Benchmarks.Environment;

/// <summary>
/// Everything a published number belongs to, captured by code so that no field is typed by hand: the machine, the
/// runtime, the GC, the revision, the libraries as loaded and every pinned package.
/// </summary>
internal static class EnvironmentManifest
{
    internal static Dictionary<string, object?> Capture()
    {
        var pinned = PackageCurrency.Pinned();

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["capturedUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["os"] = RuntimeInformation.OSDescription,
            ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["cpu"] = CpuName(),
            ["logicalCores"] = System.Environment.ProcessorCount,
            ["machine"] = Machine.Capture(),
            ["powerPlan"] = Machine.PowerPlan(),
            ["busyCpuPercentBeforeRun"] = Machine.BusyCpuPercent(TimeSpan.FromSeconds(3)),
            ["sdk"] = Shell.Run("dotnet", "--version"),
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["runtimeIdentifier"] = RuntimeInformation.RuntimeIdentifier,
            ["runtimeSettings"] = Machine.RuntimeSettings(),
            ["serverGc"] = GCSettings.IsServerGC,
            ["concurrentGc"] = AppContext.GetData("System.GC.Concurrent") is not false,
            ["gcLatencyMode"] = GCSettings.LatencyMode.ToString(),
            ["is64BitProcess"] = System.Environment.Is64BitProcess,
            ["debuggerAttached"] = Debugger.IsAttached,
            ["configuration"] = Configuration(),
            ["benchmarkDotNet"] = Version(typeof(BenchmarkDotNet.Attributes.BenchmarkAttribute).Assembly),
            ["revision"] = Shell.Git("rev-parse HEAD"),
            ["branch"] = Shell.Git("rev-parse --abbrev-ref HEAD"),
            ["dirty"] = RunTarget.WorkingTreeChanged(),
            ["libraries"] = Libraries(),
            ["packages"] = pinned,
            ["packagesOnNuGet"] = PackageCurrency.Check(pinned),
        };
    }

    internal static void Write(string path)
    {
        var manifest = Capture();
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        // An outdated pin is said before the timings, not found in the manifest afterwards.
        if (manifest["packagesOnNuGet"] is IReadOnlyDictionary<string, PackageCurrency.Entry> currency
            && PackageCurrency.Outdated(currency) is { Count: > 0 } outdated)
        {
            Console.WriteLine($"Newer stable versions exist on nuget.org: {string.Join("; ", outdated)}");
        }
    }

    /// <summary>
    /// The version of every measured library as the process loaded it, which is what a cell measured whatever a
    /// project file pinned.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> Libraries() => new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["ViShap.Rewind.Core"] = Version(typeof(Rewind).Assembly),
        ["Shielded"] = Version(typeof(Shielded.Shield).Assembly),
        ["STMSharp"] = Version(typeof(STMSharp.Core.STMEngine).Assembly),
        ["LanguageExt.Core"] = Version(typeof(LanguageExt.Prelude).Assembly),
        ["System.Collections.Immutable"] = Version(typeof(System.Collections.Immutable.ImmutableInterlocked).Assembly),
    };

    /// <summary>The informational version of an assembly, falling back to its assembly version.</summary>
    internal static string Version(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "unknown";

    private static string Configuration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    /// <summary>The processor, read from the host rather than from a library that may rename its API.</summary>
    private static string CpuName()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var name = WindowsProcessorName();
                var identifier = System.Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");

                return (name, identifier) switch
                {
                    ({ Length: > 0 }, { Length: > 0 }) => $"{name} ({identifier})",
                    ({ Length: > 0 }, _) => name,
                    (_, { Length: > 0 }) => identifier,
                    _ => "unknown",
                };
            }

            const string CpuInfo = "/proc/cpuinfo";

            if (File.Exists(CpuInfo))
            {
                foreach (var line in File.ReadLines(CpuInfo))
                {
                    if (line.StartsWith("model name", StringComparison.Ordinal))
                    {
                        return line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim();
                    }
                }
            }

            return "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    private static string WindowsProcessorName()
    {
        var output = Shell.Run("reg", @"query ""HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0"" /v ProcessorNameString");

        foreach (var line in output.Split('\n'))
        {
            var marker = line.IndexOf("REG_SZ", StringComparison.Ordinal);

            if (marker >= 0)
            {
                return line[(marker + "REG_SZ".Length)..].Trim();
            }
        }

        return string.Empty;
    }
}
