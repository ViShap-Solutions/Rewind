using System.Globalization;
using BenchmarkDotNet.Running;
using ViShap.Rewind.Benchmarks.Config;
using ViShap.Rewind.Benchmarks.Environment;
using ViShap.Rewind.Benchmarks.Market;
using ViShap.Rewind.Benchmarks.Reporting;
using ViShap.Rewind.Benchmarks.Runners;

namespace ViShap.Rewind.Benchmarks;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["--cold-child", var scenario, ..])
        {
            return ColdStartRunner.Child(scenario);
        }

        // A track carries the flags the single modes use — --filter, --soak — so it is recognized before them.
        if (args.Contains("--track", StringComparer.Ordinal))
        {
            return Track(args);
        }

        if (args.Contains("--verify", StringComparer.Ordinal))
        {
            return args.Contains("--market", StringComparer.Ordinal) ? VerifyMarket() : VerifyTrackA() | VerifyMarket();
        }

        if (args.Contains("--report", StringComparer.Ordinal))
        {
            return Report(Argument(args, "--report"));
        }

        if (args is ["--compare", var baseline, var run, ..])
        {
            return BaselineComparison.Run(baseline, run, Argument(args, "--out"));
        }

        if (args.Contains("--manifest", StringComparer.Ordinal))
        {
            Directory.CreateDirectory(Paths.Artifacts);
            var output = Path.Combine(Paths.Artifacts, "environment.json");
            EnvironmentManifest.Write(output);
            Console.WriteLine(File.ReadAllText(output));
            Console.WriteLine($"Written to {output}");
            return 0;
        }

        if (args.Contains("--cold", StringComparer.Ordinal))
        {
            return ColdStartRunner.Drive(null, (int)Number(args, "--launches", 20));
        }

        if (args.Contains("--soak", StringComparer.Ordinal))
        {
            return SoakRunner.Run(TimeSpan.FromMinutes(Number(args, "--soak", 5)), null);
        }

        if (args.Contains("--smoke", StringComparer.Ordinal))
        {
            return Smoke(args);
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, new BenchmarkConfig());
        return 0;
    }

    /// <summary>
    /// Runs a whole track: <c>--track A</c>, Rewind alone, or <c>--track B</c>, Rewind beside the alternatives.
    /// </summary>
    private static int Track(string[] args)
    {
        var track = Argument(args, "--track");

        if (!string.Equals(track, "A", StringComparison.OrdinalIgnoreCase) && !string.Equals(track, "B", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Track '{track}' has no runner. Track A measures Rewind alone; Track B measures Rewind beside the alternatives.");
            return 1;
        }

        return TrackRunner.Run(args);
    }

    /// <summary>
    /// Regenerates the report and the charts of an existing run from its raw files, so a baseline committed long ago
    /// still produces the report it produced on the day it was taken. A run whose process ended before it wrote
    /// <c>results.csv</c> is rebuilt from BenchmarkDotNet's exports first, and recorded as an interrupted measurement.
    /// </summary>
    private static int Report(string? directory)
    {
        if (directory is null || !Directory.Exists(directory))
        {
            Console.Error.WriteLine($"No run directory at '{directory}'.");
            return 1;
        }

        if (!File.Exists(Path.Combine(directory, "results.csv")))
        {
            return TrackRunner.Recover(directory);
        }

        ReportWriter.Generate(directory);
        Console.WriteLine($"Report and charts regenerated in {directory}");
        return 0;
    }

    /// <summary>Every Track A benchmark invoked once under every combination of its parameters; no scope may stay open.</summary>
    private static int VerifyTrackA()
    {
        var cases = TrackAVerifier.VerifyAll(TrackRunner.TrackASuites);
        Directory.CreateDirectory(Paths.Artifacts);
        var output = Path.Combine(Paths.Artifacts, "verification.csv");
        TrackAVerifier.Write(cases, output);

        foreach (var @case in cases.Where(@case => @case.State != ResultRow.Supported))
        {
            Console.WriteLine($"  {@case.Suite}.{@case.Method} ({@case.Parameters}): {@case.Detail}");
        }

        var failed = cases.Count(@case => @case.State != ResultRow.Supported);
        Console.WriteLine($"{cases.Count} cases, {failed} failed. Written to {output}");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>Track B's probes, tiers and pairs, with no timing.</summary>
    private static int VerifyMarket()
    {
        var verification = MarketVerifier.VerifyAll();
        var directory = Path.Combine(Paths.Artifacts, "market");
        Directory.CreateDirectory(directory);
        MarketVerifier.Write(verification, directory);

        Console.WriteLine($"{"Adapter",-22} {"Capability",-26} {"Declared",-9} {"Probed",-12} Detail");
        Console.WriteLine(new string('-', 130));

        foreach (var probe in verification.Probes)
        {
            Console.WriteLine($"{probe.Adapter,-22} {probe.Capability,-26} {probe.Declared,-9} {probe.State,-12} {probe.Detail}{(probe.Mismatch ? "   ← MISMATCH" : string.Empty)}");
        }

        Console.WriteLine();

        foreach (var pair in verification.Pairs.Where(pair => pair.State != MarketVerifier.Supported))
        {
            Console.WriteLine($"  {pair.Workload} {pair.Parameters}: {pair.State} — {pair.Detail}");
        }

        foreach (var tier in verification.Tiers.Where(placement => placement.Tier == "T1"))
        {
            Console.WriteLine($"T1 {tier.Adapter,-22} {(tier.Qualified ? "member" : "outside")} — {tier.Reason}");
        }

        Console.WriteLine();
        Console.WriteLine($"{verification.Probes.Count} probes, {verification.Pairs.Count} pairs, {verification.FailedCount} failed. Written to {directory}");
        return verification.FailedCount == 0 ? 0 : 1;
    }

    /// <summary>
    /// Runs every benchmark once, to prove each suite still starts. The numbers a smoke run produces are meaningless by
    /// construction and are never published. A <c>--filter</c> narrows it.
    /// </summary>
    private static int Smoke(string[] args)
    {
        var filters = args.Contains("--filter", StringComparer.Ordinal) ? args.Where(argument => argument != "--smoke").ToArray() : ["--filter", "*"];
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(filters, new SmokeConfig()).ToList();
        var failed = summaries.SelectMany(summary => summary.Reports).Where(report => !report.Success).ToList();

        Console.WriteLine();
        Console.WriteLine($"Smoke: {summaries.Sum(summary => summary.Reports.Length)} benchmarks, {failed.Count} failed.");

        foreach (var report in failed)
        {
            Console.WriteLine($"  failed: {report.BenchmarkCase.DisplayInfo}");
        }

        return failed.Count == 0 ? 0 : 1;
    }

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static double Number(string[] args, string name, double fallback) =>
        Argument(args, name) is { } text && double.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}
