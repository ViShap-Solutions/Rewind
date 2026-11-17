using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Async;

public sealed class FlowIsolationTests
{
    [Fact]
    public async Task SyncScope_ForeignFlowRunsInline_StaysCurrent()
    {
        var own = new RewindCell<int>(0);
        var foreignCell = new RewindCell<int>(0);
        var gate = new TaskCompletionSource();
        RewindScope? mine = null;
        RewindScope? seenByForeign = null;
        var foreignThread = 0;
        var currentAfterForeign = false;

        var (foreign, thread, failure) = OtherThread.Run(() =>
        {
            var task = Rewind.RunAsync(async () =>
            {
                await gate.Task.ConfigureAwait(false);
                seenByForeign = Rewind.Current;
                foreignThread = Environment.CurrentManagedThreadId;
                foreignCell.Value = 1;
            });

            var failure = Record.Exception(() =>
            {
                using var scope = Rewind.Begin();
                mine = scope;
                gate.SetResult();
                currentAfterForeign = Rewind.Current == scope;
                own.Value = 2;
            });

            return (task, Environment.CurrentManagedThreadId, failure);
        });

        Assert.Null(failure);
        await foreign;
        Assert.Equal(thread, foreignThread);
        Assert.True(currentAfterForeign);
        Assert.NotNull(seenByForeign);
        Assert.NotSame(mine, seenByForeign);
        Assert.Equal(0, own.Value);
        Assert.Equal(1, foreignCell.Value);
    }

    [Fact]
    public async Task AsyncScope_IsInvisibleToUnrelatedFlow()
    {
        var cell = new RewindCell<int>(0);
        var observed = false;
        var currentWasNull = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Rewind.RunAsync(async () =>
        {
            await OtherThread.Resume();
            var thread = new Thread(() =>
            {
                currentWasNull = Rewind.Current is null;
                observed = true;
                cell.Value = 1;
            });
            using (ExecutionContext.SuppressFlow())
            {
                thread.Start();
            }

            thread.Join();
            throw new InvalidOperationException();
        }));

        Assert.True(observed);
        Assert.True(currentWasNull);
        Assert.Equal(1, cell.Value);
    }

    [Fact]
    public async Task Write_WithNoScopeAfterAsynchronousScopes_AllocatesNothing()
    {
        var cell = new RewindCell<int>(0);
        var reference = new RewindCell<string>("a");
        await Rewind.RunAsync(async () =>
        {
            await OtherThread.Resume();
            cell.Value = 1;
        });

        await OtherThread.Resume();
        var value = Allocations.Measure(() => cell.Value = 2);
        var text = Allocations.Measure(() => reference.Value = "b");

        Assert.Null(Rewind.Current);
        Assert.Equal(0, value);
        Assert.Equal(0, text);
    }
}
