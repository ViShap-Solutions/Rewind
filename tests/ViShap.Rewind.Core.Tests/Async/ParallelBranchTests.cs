using ViShap.Rewind.Engine;
using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Async;

public sealed class ParallelBranchTests
{
    [Fact]
    public async Task Branch_OpensScopeWhileSiblingScopeOpen_Throws()
    {
        var cell = new RewindCell<int>(0);
        var gate = new TaskCompletionSource();
        var ran = false;
        Task? sibling = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Rewind.RunAsync(async () =>
        {
            var first = Rewind.RunAsync(async () =>
            {
                cell.Value = 1;
                await gate.Task;
            });
            sibling = Rewind.RunAsync(() =>
            {
                ran = true;
                return Task.CompletedTask;
            });
            gate.SetResult();
            await first;
            throw new InvalidOperationException();
        }));

        await Assert.ThrowsAsync<RewindScopeException>(() => sibling!);
        Assert.False(ran);
        Assert.Equal(0, cell.Value);
    }

    [Fact]
    public async Task Branch_RecordsWhileSiblingScopeOpen_Throws()
    {
        var cell = new RewindCell<int>(0);
        var other = new RewindCell<int>(0);
        var gate = new TaskCompletionSource();
        Exception? refused = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Rewind.RunAsync(async () =>
        {
            var first = Rewind.RunAsync(async () =>
            {
                cell.Value = 1;
                await gate.Task;
            });
            refused = Record.Exception(() => other.Value = 2);
            gate.SetResult();
            await first;
            throw new InvalidOperationException();
        }));

        Assert.IsType<RewindScopeException>(refused);
        Assert.Equal(0, other.Value);
        Assert.Equal(0, cell.Value);
    }

    [Fact]
    public async Task SyncScope_LeftOpenAcrossAwait_RecordingRefused()
    {
        var cell = new RewindCell<int>(0);
        Exception? refused = null;

        await Assert.ThrowsAsync<RewindScopeException>(() => Rewind.RunAsync(async () =>
        {
#pragma warning disable RWD1001 // The test leaves the scope open across an await to prove the write after it is refused.
            var leaked = Rewind.Begin();
#pragma warning restore RWD1001
            cell.Value = 1;
            await OtherThread.Resume();
            refused = Record.Exception(() => new RewindCell<int>(0).Value = 2);
            GC.KeepAlive(leaked);
        }));

        Assert.IsType<RewindScopeException>(refused);
        Assert.Equal(0, cell.Value);
    }

    [Fact]
    public async Task Recording_WhileJournalBusy_ThrowsAndLeavesJournalIntact()
    {
        var cell = new RewindCell<int>(0);
        var other = new RewindCell<int>(0);

        await Rewind.RunAsync(async scope =>
        {
            await OtherThread.Resume();
            cell.Value = 1;
            var recorded = scope.Journal.Count;

            var journal = Assert.IsType<GuardedJournal>(scope.Journal);
            Assert.True(journal.TryEnterRecording());
            var refused = Record.Exception(() => other.Value = 2);
            journal.ExitRecording();

            Assert.IsType<RewindScopeException>(refused);
            Assert.Equal(recorded, scope.Journal.Count);
            Assert.Equal(0, other.Value);
            other.Value = 3;
        });

        Assert.Equal(1, cell.Value);
        Assert.Equal(3, other.Value);
    }

    [Fact]
    public async Task Write_AfterScopeClosed_Throws()
    {
        var committed = new RewindCell<int>(0);
        var rolledBack = new RewindCell<int>(0);
        var gate = new TaskCompletionSource();
        Task? lateAfterCommit = null;
        Task? lateAfterRollback = null;

        await Rewind.RunAsync(() =>
        {
            committed.Value = 1;
            lateAfterCommit = WriteLaterAsync(committed, 2, gate.Task);
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Rewind.RunAsync(() =>
        {
            rolledBack.Value = 1;
            lateAfterRollback = WriteLaterAsync(rolledBack, 2, gate.Task);
            throw new InvalidOperationException();
        }));
        gate.SetResult();

        await Assert.ThrowsAsync<RewindScopeException>(() => lateAfterCommit!);
        await Assert.ThrowsAsync<RewindScopeException>(() => lateAfterRollback!);
        Assert.Equal(1, committed.Value);
        Assert.Equal(0, rolledBack.Value);
    }

    private static async Task WriteLaterAsync(RewindCell<int> cell, int value, Task gate)
    {
        await gate;
        cell.Value = value;
    }
}
