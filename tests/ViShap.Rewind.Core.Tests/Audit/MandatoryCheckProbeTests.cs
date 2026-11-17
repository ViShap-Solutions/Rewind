using System.Transactions;
using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Audit;

/// <summary>Audit probes for the mandatory checks of the v1 audit: transactions, flows, files.</summary>
public sealed class MandatoryCheckProbeTests
{
    [Fact]
    public void TransactionCommit_RewindEffectThrows_OtherParticipantStillCommitted()
    {
        var cell = new RewindCell<int>(0);
        var other = new RecordingParticipant();
        Exception? fromDispose;

        using (var transaction = new TransactionScope())
        {
            Rewind.Run(scope =>
            {
                cell.Value = 1;
                scope.AfterCommit(() => throw new InvalidOperationException("effect"));
            });
            Transaction.Current!.EnlistVolatile(other, EnlistmentOptions.None);
            transaction.Complete();
            fromDispose = Record.Exception(transaction.Dispose);
        }

        Assert.Equal(1, cell.Value);
        Assert.IsType<RewindEffectException>(fromDispose);
        Assert.Equal("commit", other.Outcome);
    }

    [Fact]
    public void TransactionCommit_RewindEffectSucceeds_OtherParticipantCommitted()
    {
        var cell = new RewindCell<int>(0);
        var other = new RecordingParticipant();

        using (var transaction = new TransactionScope())
        {
            Rewind.Run(scope =>
            {
                cell.Value = 1;
                scope.AfterCommit(() => { });
            });
            Transaction.Current!.EnlistVolatile(other, EnlistmentOptions.None);
            transaction.Complete();
        }

        Assert.Equal("commit", other.Outcome);
    }

    [Fact]
    public void TransactionRollback_RewindCompensationThrows_OtherParticipantStillRolledBack()
    {
        var cell = new RewindCell<int>(0);
        var other = new RecordingParticipant();
        Exception? fromDispose;

        using (var transaction = new TransactionScope())
        {
            Rewind.Run(scope =>
            {
                cell.Value = 1;
                scope.Effect(() => { }, () => throw new InvalidOperationException("compensation"));
            });
            Transaction.Current!.EnlistVolatile(other, EnlistmentOptions.None);
            fromDispose = Record.Exception(transaction.Dispose);
        }

        Assert.IsType<RewindCompensationException>(fromDispose);
        Assert.Equal(0, cell.Value);
        Assert.Equal("rollback", other.Outcome);
    }

    [Fact]
    public void PendingScope_TransactionRollsBackAfterLaterCommit_RestoresOlderValueOverIt()
    {
        var cell = new RewindCell<int>(0);

        using (new TransactionScope())
        {
            Rewind.Run(() => cell.Value = 1);
            using (new TransactionScope(TransactionScopeOption.Suppress))
            {
                Rewind.Run(() => cell.Value = 2);
            }

            Assert.Equal(2, cell.Value);
        }

        Assert.Equal(0, cell.Value);
    }

    [Fact]
    public void PendingScope_TransactionRolledBackOnAnotherThread_RollbackRunsThere()
    {
        var cell = new RewindCell<int>(0);
        var rollbackThread = 0;
        Exception? onOtherThread;

        using (new TransactionScope())
        {
            var ambient = Transaction.Current!;
            Rewind.Run(scope =>
            {
                cell.Value = 1;
                scope.AfterRollback(() => rollbackThread = Environment.CurrentManagedThreadId);
            });

            onOtherThread = OtherThread.Capture(ambient.Rollback);

            Assert.Equal(0, cell.Value);
            Assert.NotEqual(Environment.CurrentManagedThreadId, rollbackThread);
            Assert.NotEqual(0, rollbackThread);
            Assert.Null(Rewind.Current);
        }

        Assert.Null(onOtherThread);
    }

    [Fact]
    public void PendingScope_RolledBackOnAnotherThreadAndCompensationThrows_NothingThrownThereOthersStillRun()
    {
        var cell = new RewindCell<int>(0);
        var compensated = false;
        Exception? onOtherThread;

        using (new TransactionScope())
        {
            var ambient = Transaction.Current!;
            Rewind.Run(scope =>
            {
                cell.Value = 1;
                scope.Effect(() => { }, () => compensated = true);
                scope.Effect(() => { }, () => throw new InvalidOperationException("compensation"));
            });

            onOtherThread = OtherThread.Capture(ambient.Rollback);
        }

        Assert.Null(onOtherThread);
        Assert.Equal(0, cell.Value);
        Assert.True(compensated);
    }

    [Fact]
    public async Task Flows_ManyRandomFlowsWithNestedScopes_EachEndsAsItsOutcome()
    {
        const int flows = 2000;
        var cells = Enumerable.Range(0, flows).Select(_ => new RewindCell<int>(0)).ToArray();
        var lists = Enumerable.Range(0, flows).Select(_ => new RewindList<int>()).ToArray();

        var tasks = Enumerable.Range(0, flows).Select(i => Task.Run(async () =>
        {
            var random = new Random(i);
            try
            {
                await Rewind.RunAsync(async () =>
                {
                    for (var step = 0; step < 4; step++)
                    {
                        cells[i].Value += 1;
                        lists[i].Add(step);
                        switch (random.Next(4))
                        {
                            case 0:
                                await Task.Yield();
                                break;
                            case 1:
                                await Task.Delay(random.Next(2)).ConfigureAwait(false);
                                break;
                            case 2:
                                Rewind.Run(() => cells[i].Value += 100);
                                break;
                            default:
                                await Rewind.RunAsync(async () =>
                                {
                                    await Task.Yield();
                                    lists[i].Add(-1);
                                });
                                break;
                        }
                    }

                    if (i % 2 == 1)
                    {
                        throw new InvalidOperationException();
                    }
                });
            }
            catch (InvalidOperationException)
            {
            }

            return Rewind.Current is null;
        })).ToArray();

        var noScopeLeft = await Task.WhenAll(tasks);

        Assert.All(noScopeLeft, Assert.True);
        for (var i = 1; i < flows; i += 2)
        {
            Assert.Equal(0, cells[i].Value);
            Assert.Empty(lists[i]);
        }

        for (var i = 0; i < flows; i += 2)
        {
            Assert.True(cells[i].Value >= 4);
            Assert.True(lists[i].Count >= 4);
        }
    }

    [Fact]
    public void Flow_OnSingleThreadedSynchronizationContext_RollsBackAndCommits()
    {
        var cell = new RewindCell<int>(0);
        var list = new RewindList<int>();
        var outcome = SingleThreadContext.Run(async () =>
        {
            var thread = Environment.CurrentManagedThreadId;
            try
            {
                await Rewind.RunAsync(async () =>
                {
                    cell.Value = 1;
                    await Task.Yield();
                    using (var scope = Rewind.Begin())
                    {
                        list.Add(1);
                        scope.Commit();
                    }

                    await Task.Delay(1);
                    cell.Value = 2;
                    throw new InvalidOperationException();
                });
            }
            catch (InvalidOperationException)
            {
            }

            var afterRollback = (cell.Value, list.Count);
            await Rewind.RunAsync(async () =>
            {
                await Task.Yield();
                cell.Value = 3;
            });
            return (afterRollback, cell.Value, Environment.CurrentManagedThreadId == thread, Rewind.Current is null);
        });

        Assert.Equal(((0, 0), 3, true, true), outcome);
    }

    [Fact]
    public void RewindFile_TargetHeldWithoutShareDelete_FailsAtCallAndScopeRollsBackWhole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TempDirectory();
        var path = directory.File("held.txt");
        File.WriteAllText(path, "old");
        var cell = new RewindCell<int>(0);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<IOException>(() => Rewind.Run(() =>
            {
                cell.Value = 1;
                RewindFile.WriteAllText(path, "new");
            }));
        }

        Assert.Equal(0, cell.Value);
        Assert.Equal("old", File.ReadAllText(path));
        Assert.Empty(directory.SideFiles());
    }

    private sealed class RecordingParticipant : IEnlistmentNotification
    {
        public string Outcome { get; private set; } = "none";

        public void Prepare(PreparingEnlistment preparingEnlistment) => preparingEnlistment.Prepared();

        public void Commit(Enlistment enlistment)
        {
            Outcome = "commit";
            enlistment.Done();
        }

        public void Rollback(Enlistment enlistment)
        {
            Outcome = "rollback";
            enlistment.Done();
        }

        public void InDoubt(Enlistment enlistment)
        {
            Outcome = "in doubt";
            enlistment.Done();
        }
    }

    /// <summary>Runs an asynchronous function on a dedicated thread whose synchronization context posts back to that thread.</summary>
    private sealed class SingleThreadContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> _queue = [];

        public static T Run<T>(Func<Task<T>> function) => OtherThread.Run(() =>
        {
            var context = new SingleThreadContext();
            SetSynchronizationContext(context);
            var task = function();
            task.ContinueWith(_ => context._queue.CompleteAdding(), TaskScheduler.Default);
            foreach (var (callback, state) in context._queue.GetConsumingEnumerable())
            {
                callback(state);
            }

            return task.GetAwaiter().GetResult();
        });

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
    }
}
