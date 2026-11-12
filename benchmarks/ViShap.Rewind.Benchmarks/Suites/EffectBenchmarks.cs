using BenchmarkDotNet.Attributes;

namespace ViShap.Rewind.Benchmarks.Suites;

/// <summary>
/// W6 — K effects registered in a scope: <c>AfterCommit</c> run after the commit, <c>Effect</c> committed, and
/// <c>Effect</c> compensated by a rollback. The actions are static and count into a field.
/// </summary>
[Config(typeof(TrackAConfig))]
public class EffectBenchmarks
{
    private static readonly Action s_increment = static () => s_counter++;
    private static int s_counter;

    [Params(1, 16)]
    public int Effects { get; set; }

    [Benchmark(Baseline = true)]
    public int EmptyScope()
    {
        using var scope = Rewind.Begin();
        scope.Commit();
        return s_counter;
    }

    [Benchmark]
    public int AfterCommitRun()
    {
        using var scope = Rewind.Begin();
        for (var i = 0; i < Effects; i++)
        {
            scope.AfterCommit(s_increment);
        }

        scope.Commit();
        return s_counter;
    }

    [Benchmark]
    public int EffectCommitted()
    {
        using var scope = Rewind.Begin();
        for (var i = 0; i < Effects; i++)
        {
            scope.Effect(s_increment, s_increment);
        }

        scope.Commit();
        return s_counter;
    }

    [Benchmark]
    public int EffectCompensated()
    {
        using (var scope = Rewind.Begin())
        {
            for (var i = 0; i < Effects; i++)
            {
                scope.Effect(s_increment, s_increment);
            }
        }

        return s_counter;
    }
}
