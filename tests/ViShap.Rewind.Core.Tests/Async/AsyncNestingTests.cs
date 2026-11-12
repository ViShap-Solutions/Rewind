using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Async;

public sealed class AsyncNestingTests
{
    [Fact]
    public async Task SyncRunInsideRunAsync_NestsAsChild()
    {
        var cell = new RewindCell<int>(0);
        var other = new RewindCell<int>(0);
        var afterChild = -1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Rewind.RunAsync(async scope =>
        {
            cell.Value = 1;
            await OtherThread.Resume();
            Assert.Throws<ArgumentException>(() => Rewind.Run(() =>
            {
                Assert.Equal(1, Rewind.Current!.Depth);
                cell.Value = 2;
                throw new ArgumentException();
            }));
            afterChild = cell.Value;
            Rewind.Run(() => other.Value = 5);
            Assert.Same(scope, Rewind.Current);
            throw new InvalidOperationException();
        }));

        Assert.Equal(1, afterChild);
        Assert.Equal(0, cell.Value);
        Assert.Equal(0, other.Value);
    }

    [Fact]
    public async Task RunAsyncInsideRunAsync_NestsAsChild()
    {
        var cell = new RewindCell<int>(0);
        var other = new RewindCell<int>(0);
        var afterChild = -1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Rewind.RunAsync(async scope =>
        {
            cell.Value = 1;
            await Assert.ThrowsAsync<ArgumentException>(() => Rewind.RunAsync(async () =>
            {
                await OtherThread.Resume();
                Assert.Equal(1, Rewind.Current!.Depth);
                cell.Value = 2;
                throw new ArgumentException();
            }));
            afterChild = cell.Value;
            await Rewind.RunAsync(async () =>
            {
                await OtherThread.Resume();
                other.Value = 5;
            });
            Assert.Same(scope, Rewind.Current);
            throw new InvalidOperationException();
        }));

        Assert.Equal(1, afterChild);
        Assert.Equal(0, cell.Value);
        Assert.Equal(0, other.Value);
    }

    [Fact]
    public async Task RunAsync_InsideSynchronousScope_ThrowsBeforeRunning()
    {
        var cell = new RewindCell<int>(0);
        var ran = false;
        Task? fromBegin;
        Task? fromRun = null;

        using (var scope = Rewind.Begin())
        {
            fromBegin = Rewind.RunAsync(() =>
            {
                ran = true;
                return Task.CompletedTask;
            });
            Rewind.Run(() =>
            {
                fromRun = Rewind.RunAsync(() =>
                {
                    ran = true;
                    return Task.CompletedTask;
                });
            });
            Assert.Same(scope, Rewind.Current);
            cell.Value = 1;
            scope.Commit();
        }

        await Assert.ThrowsAsync<RewindScopeException>(() => fromBegin);
        await Assert.ThrowsAsync<RewindScopeException>(() => fromRun!);
        Assert.False(ran);
        Assert.Equal(1, cell.Value);
    }
}
