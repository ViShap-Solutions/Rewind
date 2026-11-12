using System.Transactions;
using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Async;

public sealed class RunAsyncTests
{
    [Fact]
    public async Task RunAsync_BlockCompletes_CommitsWritesAcrossAwaits()
    {
        var cell = new RewindCell<int>(0);
        var list = new RewindList<string>();

        await Rewind.RunAsync(async () =>
        {
            cell.Value = 1;
            await OtherThread.Resume();
            cell.Value = 2;
            await OtherThread.Resume();
            list.Add("a");
        });

        Assert.Equal(2, cell.Value);
        Assert.Equal(new[] { "a" }, list);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public async Task RunAsync_BlockThrowsAfterAwait_RollsBackAndRethrowsSameException()
    {
        var cell = new RewindCell<int>(0);
        var list = new RewindList<string> { "a" };
        var thrown = new InvalidOperationException();

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => Rewind.RunAsync(async () =>
        {
            cell.Value = 1;
            await OtherThread.Resume();
            list.Clear();
            cell.Value = 2;
            await OtherThread.Resume();
            throw thrown;
        }));

        Assert.Same(thrown, caught);
        Assert.Equal(0, cell.Value);
        Assert.Equal(new[] { "a" }, list);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public async Task Current_FlowsThroughAwaitsAndIsRestoredAfter()
    {
        RewindScope? outer = null;
        var seen = new List<(RewindScope? Scope, int Thread)>();
        RewindScope? afterInner = null;

        await Rewind.RunAsync(async scope =>
        {
            outer = scope;
            seen.Add((Rewind.Current, Environment.CurrentManagedThreadId));
            await OtherThread.Resume();
            seen.Add((Rewind.Current, Environment.CurrentManagedThreadId));
            await Rewind.RunAsync(async () =>
            {
                await OtherThread.Resume();
                Assert.NotSame(scope, Rewind.Current);
            });
            afterInner = Rewind.Current;
        });

        Assert.All(seen, observed => Assert.Same(outer, observed.Scope));
        Assert.NotEqual(seen[0].Thread, seen[1].Thread);
        Assert.Same(outer, afterInner);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public async Task RunAsync_WithScope_EffectsFollowOutcomeWithNoCurrentScope()
    {
        var cell = new RewindCell<int>(0);
        var log = new List<string>();

        await Rewind.RunAsync(async scope =>
        {
            cell.Value = 1;
            await OtherThread.Resume();
            scope.AfterCommit(() => log.Add($"commit {cell.Value} {Rewind.Current is null}"));
            scope.AfterRollback(() => log.Add("rollback"));
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Rewind.RunAsync(async scope =>
        {
            cell.Value = 2;
            await OtherThread.Resume();
            scope.AfterCommit(() => log.Add("commit"));
            scope.AfterRollback(() => log.Add($"rollback {cell.Value} {Rewind.Current is null}"));
            throw new InvalidOperationException();
        }));

        Assert.Equal(new[] { "commit 1 True", "rollback 1 True" }, log);
    }

    [Fact]
    public async Task RunAsync_WithResult_ReturnsResultAndCommits()
    {
        var cell = new RewindCell<int>(0);

        var first = await Rewind.RunAsync(async () =>
        {
            await OtherThread.Resume();
            cell.Value = 1;
            return "first";
        });
        var second = await Rewind.RunAsync(async scope =>
        {
            await OtherThread.Resume();
            cell.Value = 2;
            return scope.Depth;
        });

        Assert.Equal("first", first);
        Assert.Equal(0, second);
        Assert.Equal(2, cell.Value);
    }

    [Fact]
    public async Task RunAsync_BlockCanceled_RollsBackAndTaskIsCanceled()
    {
        var cell = new RewindCell<int>(0);
        using var cancellation = new CancellationTokenSource();

        var task = Rewind.RunAsync(async () =>
        {
            cell.Value = 1;
            await OtherThread.Resume();
            await cancellation.CancelAsync();
            cancellation.Token.ThrowIfCancellationRequested();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(task.IsCanceled);
        Assert.Equal(0, cell.Value);
    }

    [Fact]
    public async Task RunAsync_InvalidBlocks_Refused()
    {
        var cell = new RewindCell<int>(0);

        Assert.Throws<ArgumentNullException>(() => { _ = Rewind.RunAsync((Func<Task>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = Rewind.RunAsync((Func<RewindScope, Task>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = Rewind.RunAsync((Func<Task<int>>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = Rewind.RunAsync((Func<RewindScope, Task<int>>)null!); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Rewind.RunAsync(() =>
        {
            cell.Value = 1;
            return null!;
        }));

        Assert.Equal(0, cell.Value);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public async Task RunAsync_InAmbientTransaction_FollowsItsOutcome()
    {
        var cell = new RewindCell<int>(0);

        using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await Rewind.RunAsync(async () =>
            {
                await OtherThread.Resume();
                cell.Value = 1;
            });
            Assert.Equal(1, cell.Value);
        }

        Assert.Equal(0, cell.Value);

        using (var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await Rewind.RunAsync(async () =>
            {
                await OtherThread.Resume();
                cell.Value = 2;
            });
            transaction.Complete();
        }

        Assert.Equal(2, cell.Value);
    }
}
