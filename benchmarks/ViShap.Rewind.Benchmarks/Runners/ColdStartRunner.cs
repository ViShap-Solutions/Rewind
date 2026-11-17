using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using ViShap.Rewind.Benchmarks.Environment;
using ViShap.Rewind.Benchmarks.Suites;

namespace ViShap.Rewind.Benchmarks.Runners;

/// <summary>
/// Cold start: the first use of each mechanism in a fresh process, where the type loading, the static constructors
/// and the JIT of the path are paid. A first call after a warm-up is not cold, so each measurement is its own process:
/// the harness launches one, the child performs exactly one operation and prints what it took, and the aggregate is
/// over launches. A child that does nothing measures process start-up itself, published beside the rest.
/// </summary>
internal static class ColdStartRunner
{
    /// <summary>The scenarios, in the order the table lists them, each with what its one operation is.</summary>
    internal static readonly (string Scenario, string Description)[] Scenarios =
    [
        ("empty", "nothing — the process start-up every other row also pays"),
        ("cell-write", "one write to a RewindCell<int> with no scope"),
        ("run-commit", "Rewind.Run with one write, committed"),
        ("begin-rollback", "Rewind.Begin, one write, disposed without commit"),
        ("generated-property", "one write to a [Rewindable] property in a committed scope"),
        ("list-rollback", "one RewindList<int>.Add in a scope rolled back"),
        ("run-async", "Rewind.RunAsync with one write and a block that completes at once"),
    ];

    /// <summary>Runs one scenario inside a freshly started process and prints it as one CSV line.</summary>
    /// <remarks>
    /// The operation is chosen and the clock and the allocation counter are touched before the measurement, and each
    /// scenario is a method of its own that is never inlined, so the number holds the first use of that scenario's
    /// path and nothing of the harness or of another scenario.
    /// </remarks>
    internal static int Child(string scenario)
    {
        var startedAt = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        Action operation = Operation(scenario);
        _ = Stopwatch.GetTimestamp();
        _ = GC.GetTotalAllocatedBytes(precise: true);

        var beforeOperation = DateTime.UtcNow;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();

        operation();

        timer.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{(beforeOperation - startedAt).TotalMilliseconds:F3},{timer.Elapsed.TotalMicroseconds:F1},{allocated},{System.Environment.WorkingSet}"));

        return 0;
    }

    /// <summary>Launches the children, aggregates over launches, and writes <c>cold-start.csv</c>.</summary>
    internal static int Drive(string? outputDirectory, int launches)
    {
        var rows = new StringBuilder(
            "scenario,description,launches,startup_ms_median,first_operation_us_median,first_operation_us_min," +
            "first_operation_us_p95,first_operation_us_max,allocated_bytes_median,working_set_median\n");

        foreach (var (scenario, description) in Scenarios)
        {
            var startup = new List<double>(launches);
            var elapsed = new List<double>(launches);
            var allocated = new List<double>(launches);
            var workingSet = new List<double>(launches);

            for (var launch = 0; launch < launches; launch++)
            {
                var line = Launch($"--cold-child {scenario}");
                var parts = line.Split(',');

                if (parts.Length != 4)
                {
                    Console.Error.WriteLine($"cold start: unusable output for {scenario}: {line}");
                    continue;
                }

                startup.Add(double.Parse(parts[0], CultureInfo.InvariantCulture));
                elapsed.Add(double.Parse(parts[1], CultureInfo.InvariantCulture));
                allocated.Add(double.Parse(parts[2], CultureInfo.InvariantCulture));
                workingSet.Add(double.Parse(parts[3], CultureInfo.InvariantCulture));
            }

            if (elapsed.Count == 0)
            {
                continue;
            }

            rows.Append(CultureInfo.InvariantCulture,
                $"{scenario},\"{description}\",{elapsed.Count},{Percentile(startup, 50):F3},{Percentile(elapsed, 50):F1},");
            rows.Append(CultureInfo.InvariantCulture,
                $"{elapsed.Min():F1},{Percentile(elapsed, 95):F1},{elapsed.Max():F1},{Percentile(allocated, 50):F0},{Percentile(workingSet, 50):F0}\n");

            Console.WriteLine($"  {scenario,-20} first operation {Percentile(elapsed, 50),10:F1} µs   start-up {Percentile(startup, 50),8:F1} ms");
        }

        var destination = outputDirectory ?? Paths.Artifacts;
        Directory.CreateDirectory(destination);
        var output = Path.Combine(destination, "cold-start.csv");
        File.WriteAllText(output, rows.ToString(), Encoding.UTF8);

        Console.WriteLine($"Written to {output}");
        return 0;
    }

    private static Action Operation(string scenario) => scenario switch
    {
        "empty" => Empty,
        "cell-write" => CellWrite,
        "run-commit" => RunCommit,
        "begin-rollback" => BeginRollback,
        "generated-property" => GeneratedProperty,
        "list-rollback" => ListRollback,
        "run-async" => RunAsync,
        _ => throw new ArgumentException($"No cold-start scenario is named '{scenario}'.", nameof(scenario)),
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Empty()
    {
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CellWrite()
    {
        var cell = new RewindCell<int>(0);
        cell.Value = 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunCommit()
    {
        var cell = new RewindCell<int>(0);
        Rewind.Run(() => cell.Value = 1);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BeginRollback()
    {
        var cell = new RewindCell<int>(0);
        using (Rewind.Begin())
        {
            cell.Value = 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void GeneratedProperty()
    {
        var account = new Account();
        using var scope = Rewind.Begin();
        account.Balance = 1m;
        scope.Commit();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ListRollback()
    {
        var list = new RewindList<int>();
        using (Rewind.Begin())
        {
            list.Add(1);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunAsync()
    {
        var cell = new RewindCell<int>(0);
        Rewind.RunAsync(() =>
        {
            cell.Value = 1;
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
    }

    /// <summary>Starts this program again with <paramref name="arguments"/> and returns what it printed.</summary>
    internal static string Launch(string arguments)
    {
        var executable = System.Environment.ProcessPath ?? "dotnet";
        var assembly = System.Reflection.Assembly.GetEntryAssembly()!.Location;

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };

        // A `dotnet` host needs the assembly path; an apphost already is the program.
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(assembly);
        }

        foreach (var argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        return output;
    }

    private static double Percentile(List<double> values, double percent)
    {
        var ordered = values.Order().ToArray();
        var rank = percent / 100 * (ordered.Length - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        return ordered[low] + ((ordered[high] - ordered[low]) * (rank - low));
    }
}
