using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Validators;
using Perfolizer.Horology;

namespace ViShap.Rewind.Benchmarks.Config;

/// <summary>Which job a suite is measured under.</summary>
internal enum JobKind
{
    /// <summary>Throughput, 8 warmup and 20 measured iterations, one launch: every published cell.</summary>
    Publication,

    /// <summary>One warmup and three iterations of 50 ms: proves the pipeline, never published as a number.</summary>
    Shortened,

    /// <summary>BenchmarkDotNet's default job, for a suite whose cells must stay comparable with earlier findings.</summary>
    Default,
}

/// <summary>
/// The configuration every suite runs under. Warmup, iteration and launch counts are stated here rather than
/// inherited from a default, because a baseline outlives the tool version that produced it.
/// </summary>
/// <remarks>
/// A shortened job exists for one purpose: proving that a whole track produces every artifact without spending an
/// hour on it. A run that uses it is a measurement by construction and can never become a baseline.
/// </remarks>
internal sealed class BenchmarkConfig : ManualConfig
{
    internal BenchmarkConfig()
        : this(Environment.Paths.Artifacts, JobKind.Publication)
    {
    }

    internal BenchmarkConfig(string artifactsPath, JobKind kind)
    {
        AddJob(kind switch
        {
            JobKind.Shortened => Job.Default
                .WithId("Shortened")
                .WithStrategy(RunStrategy.Throughput)
                .WithWarmupCount(1)
                .WithIterationCount(3)
                .WithLaunchCount(1)
                .WithIterationTime(TimeInterval.FromMilliseconds(50)),
            JobKind.Default => Job.Default.WithId("Default"),
            _ => Job.Default
                .WithId("Publication")
                .WithStrategy(RunStrategy.Throughput)
                .WithWarmupCount(8)
                .WithIterationCount(20)
                .WithLaunchCount(1),
        });

        AddDiagnoser(MemoryDiagnoser.Default);

        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.Error);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.Median);
        AddColumn(StatisticColumn.P95);
        AddColumn(StatisticColumn.OperationsPerSecond);
        AddColumnProvider(DefaultColumnProviders.Instance);

        AddExporter(CsvExporter.Default);
        AddExporter(JsonExporter.Full);
        AddExporter(MarkdownExporter.GitHub);

        AddLogger(ConsoleLogger.Default);

        // A published number never comes from an unoptimized build.
        AddValidator(JitOptimizationsValidator.FailOnError);

        Orderer = new DefaultOrderer(SummaryOrderPolicy.Declared);

        WithArtifactsPath(artifactsPath);
        WithOptions(ConfigOptions.DisableLogFile);
    }
}

/// <summary>
/// Marks a suite measured under BenchmarkDotNet's default job instead of the publication job, so its cells stay
/// comparable with the findings recorded against it.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class KeepsDefaultJobAttribute : Attribute;
