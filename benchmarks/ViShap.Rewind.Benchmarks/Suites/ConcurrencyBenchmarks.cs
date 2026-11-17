using BenchmarkDotNet.Attributes;

namespace ViShap.Rewind.Benchmarks.Suites;

/// <summary>
/// W14 — independent scopes on T threads at once: every thread opens <see cref="ScopesPerThread"/> scopes of four
/// writes to cells of its own and commits or rolls back each. Nothing is shared but the engine itself, so the curve
/// shows what the engine's process-wide state costs under parallel use.
/// </summary>
/// <remarks>
/// The threads are dedicated and live for the whole suite; an invocation releases them and waits for all of them to
/// finish, so thread start-up is not timed. The time of one invocation is the time of the slowest thread.
/// </remarks>
public class ConcurrencyBenchmarks
{
    /// <summary>The scopes each thread opens in one invocation.</summary>
    public const int ScopesPerThread = 20_000;

    private Worker[] _workers = [];
    private CountdownEvent _done = null!;

    public static IEnumerable<int> ThreadCounts =>
        new[] { 1, 2, 4, 8, System.Environment.ProcessorCount }.Where(count => count <= System.Environment.ProcessorCount).Distinct().Order();

    [ParamsSource(nameof(ThreadCounts))]
    public int Threads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _done = new CountdownEvent(Threads);
        _workers = [.. Enumerable.Range(0, Threads).Select(index => new Worker(index, _done))];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var worker in _workers)
        {
            worker.Stop();
        }

        _done.Dispose();
    }

    [Benchmark]
    public int CommitOnEveryThread() => Run(rollback: false);

    [Benchmark]
    public int RollbackOnEveryThread() => Run(rollback: true);

    private int Run(bool rollback)
    {
        _done.Reset(Threads);

        foreach (var worker in _workers)
        {
            worker.Release(rollback);
        }

        _done.Wait();
        return _workers[0].Result;
    }

    private sealed class Worker
    {
        private readonly RewindCell<int>[] _cells = [new(0), new(0), new(0), new(0)];
        private readonly AutoResetEvent _go = new(false);
        private readonly CountdownEvent _done;
        private readonly Thread _thread;
        private volatile bool _rollback;
        private volatile bool _stopping;

        internal Worker(int index, CountdownEvent done)
        {
            _done = done;
            _thread = new Thread(Loop) { IsBackground = true, Name = $"W14 worker {index}" };
            _thread.Start();
        }

        internal int Result { get; private set; }

        internal void Release(bool rollback)
        {
            _rollback = rollback;
            _go.Set();
        }

        internal void Stop()
        {
            _stopping = true;
            _go.Set();
            _thread.Join();
            _go.Dispose();
        }

        private void Loop()
        {
            while (true)
            {
                _go.WaitOne();

                if (_stopping)
                {
                    return;
                }

                var cells = _cells;
                var rollback = _rollback;

                for (var i = 0; i < ScopesPerThread; i++)
                {
                    using var scope = Rewind.Begin();
                    cells[0].Value = i;
                    cells[1].Value = i;
                    cells[2].Value = i;
                    cells[3].Value = i;

                    if (!rollback)
                    {
                        scope.Commit();
                    }
                }

                Result = cells[0].Value;
                _done.Signal();
            }
        }
    }
}
