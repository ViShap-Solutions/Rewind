using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Validators;

namespace ViShap.Rewind.Benchmarks.Config;

/// <summary>
/// The job of a smoke run: one invocation per benchmark, to prove every suite still starts. Its numbers are
/// meaningless and never published, which is why it writes to a directory of its own.
/// </summary>
internal sealed class SmokeConfig : ManualConfig
{
    internal SmokeConfig()
    {
        AddJob(Job.Dry
            .WithId("Smoke")
            .WithStrategy(RunStrategy.ColdStart)
            .WithWarmupCount(1)
            .WithIterationCount(1)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .WithLaunchCount(1));

        AddColumnProvider(DefaultColumnProviders.Instance);
        AddLogger(ConsoleLogger.Default);
        AddValidator(JitOptimizationsValidator.FailOnError);

        Orderer = new DefaultOrderer(SummaryOrderPolicy.Declared);

        WithArtifactsPath(Path.Combine(Environment.Paths.Artifacts, "smoke"));
        WithOptions(ConfigOptions.DisableLogFile | ConfigOptions.JoinSummary);
    }
}
