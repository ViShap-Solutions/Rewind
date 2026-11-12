using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;

namespace ViShap.Rewind.Benchmarks;

/// <summary>
/// The job every Track A suite runs with: throughput, explicit warmup and iteration counts, memory measured.
/// </summary>
public sealed class TrackAConfig : ManualConfig
{
    /// <summary>Creates the configuration.</summary>
    public TrackAConfig()
    {
        AddJob(Job.Default
            .WithStrategy(RunStrategy.Throughput)
            .WithWarmupCount(8)
            .WithIterationCount(20)
            .WithId("TrackA"));
        AddDiagnoser(MemoryDiagnoser.Default);
    }
}
