using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ViShap.Rewind.Benchmarks.Environment;

/// <summary>
/// The facts of a run the runtime does not report itself: physical cores, clock, memory, the power plan, a hypervisor,
/// the runtime switches a run inherits, and how busy the machine was before the run started. Every field is
/// best-effort: one that cannot be read is <c>unknown</c>, never a reason to stop a run.
/// </summary>
internal static class Machine
{
    private const string Unknown = "unknown";

    /// <summary>Cores, clock, memory and hypervisor, read from the host's own inventory.</summary>
    internal static object Capture()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["memoryBytes"] = LinuxMemoryBytes(),
            };
        }

        const string Script =
            "$p = Get-CimInstance Win32_Processor; " +
            "$m = Get-CimInstance Win32_PhysicalMemory; " +
            "$c = Get-CimInstance Win32_ComputerSystem; " +
            "[pscustomobject]@{ " +
            "physicalCores = ($p | Measure-Object NumberOfCores -Sum).Sum; " +
            "maxClockMHz = ($p | Select-Object -First 1).MaxClockSpeed; " +
            "memoryBytes = $c.TotalPhysicalMemory; " +
            "memoryModules = @($m).Count; " +
            "memoryConfiguredMHz = ($m | Select-Object -First 1).ConfiguredClockSpeed; " +
            "hypervisorPresent = $c.HypervisorPresent; " +
            "model = $c.Model } | ConvertTo-Json -Compress";

        var output = Shell.Run("powershell", $"-NoProfile -NonInteractive -EncodedCommand {Encode(Script)}");

        try
        {
            return string.IsNullOrWhiteSpace(output)
                ? Unknown
                : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(output) ?? (object)Unknown;
        }
        catch (JsonException)
        {
            return Unknown;
        }
    }

    /// <summary>The active power scheme as the host names it, or the CPU governor on Linux.</summary>
    internal static string PowerPlan()
    {
        if (OperatingSystem.IsWindows())
        {
            var scheme = Shell.Run("powercfg", "/getactivescheme");
            return string.IsNullOrWhiteSpace(scheme) ? Unknown : scheme;
        }

        const string Governor = "/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor";
        return File.Exists(Governor) ? File.ReadAllText(Governor).Trim() : Unknown;
    }

    /// <summary>
    /// The share of all logical processors other processes used over <paramref name="window"/>, sampled before
    /// anything is measured. A busy machine is recorded, not refused: the number says how far the run's timings can
    /// be trusted.
    /// </summary>
    internal static double? BusyCpuPercent(TimeSpan window)
    {
        try
        {
            int self = System.Environment.ProcessId;
            var before = Sample(self);
            var clock = Stopwatch.StartNew();

            Thread.Sleep(window);

            var after = Sample(self);
            double elapsed = clock.Elapsed.TotalMilliseconds * System.Environment.ProcessorCount;
            double used = after.Sum(entry => before.TryGetValue(entry.Key, out var start)
                ? Math.Max(0, (entry.Value - start).TotalMilliseconds)
                : 0);

            return Math.Round(100 * used / elapsed, 1);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The runtime switches a benchmark process inherits from the environment — tiered compilation, PGO, the GC —
    /// named as the runtime reads them. An empty set means every one is at its default.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> RuntimeSettings() =>
        System.Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Select(entry => (Name: entry.Key.ToString() ?? string.Empty, Value: entry.Value?.ToString() ?? string.Empty))
            .Where(entry => entry.Name.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)
                            || entry.Name.StartsWith("COMPlus_", StringComparison.OrdinalIgnoreCase))
            .Where(entry => !entry.Name.Contains("CLI_", StringComparison.OrdinalIgnoreCase)
                            && !entry.Name.Contains("NOLOGO", StringComparison.OrdinalIgnoreCase)
                            && !entry.Name.Contains("ROOT", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);

    private static Dictionary<int, TimeSpan> Sample(int self)
    {
        var sample = new Dictionary<int, TimeSpan>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == self)
                {
                    continue;
                }

                try
                {
                    sample[process.Id] = process.TotalProcessorTime;
                }
                catch (Exception)
                {
                    // A process this user may not read, or one that exited between the list and the read.
                }
            }
        }

        return sample;
    }

    private static object LinuxMemoryBytes()
    {
        const string MemInfo = "/proc/meminfo";

        if (!File.Exists(MemInfo))
        {
            return Unknown;
        }

        var line = File.ReadLines(MemInfo).FirstOrDefault(text => text.StartsWith("MemTotal:", StringComparison.Ordinal));
        var digits = new string(line?.Where(char.IsDigit).ToArray() ?? []);

        return long.TryParse(digits, out long kib) ? kib * 1024 : Unknown;
    }

    private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
}
