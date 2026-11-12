using ViShap.Rewind.Testing;
using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Verify;

public sealed class AsyncVerifyTests
{
    [Fact]
    public async Task RollbackAsync_AtomicOperation_Passes()
    {
        var order = new Order { Status = "new", Total = 0m };
        var lines = new RewindList<string>();

        await RewindVerify.RollbackAsync(
            () => Rewind.RunAsync(async scope =>
            {
                order.Status = "paid";
                await OtherThread.Resume();
                lines.Add("a");
                await Rewind.RunAsync(async () =>
                {
                    await OtherThread.Resume();
                    order.Total = 10m;
                });
                scope.AfterCommit(() => lines.Add("late"));
            }),
            () => (order.Status, order.Total, lines.Count));

        Assert.Equal("new", order.Status);
        Assert.Empty(lines);
    }

    [Fact]
    public async Task RollbackAsync_TwoScopesFirstCommits_Reported()
    {
        var order = new Order { Status = "new", Total = 0m };

        var exception = await Assert.ThrowsAsync<RewindVerificationException>(() => RewindVerify.RollbackAsync(
            async () =>
            {
                await Rewind.RunAsync(async () =>
                {
                    await OtherThread.Resume();
                    order.Total = 10m;
                });
                await Rewind.RunAsync(async () =>
                {
                    await OtherThread.Resume();
                    order.Status = "paid";
                });
            },
            () => (order.Status, order.Total)));

        Assert.Equal(2, exception.FailurePoint);
        Assert.Equal("write to Order.Status", exception.Point);
    }

    [Fact]
    public async Task RetriesAsync_ResultDependsOnUntrackedCounter_Reported()
    {
        var cell = new RewindCell<int>(0);
        var calls = 0;

        var exception = await Assert.ThrowsAsync<RewindVerificationException>(() => RewindVerify.RetriesAsync(
            () => Rewind.RunAsync(async () =>
            {
                await OtherThread.Resume();
                cell.Value = 1;
                return ++calls;
            }),
            2,
            () => cell.Value));

        Assert.Contains("Attempt 2 disagrees", exception.Message);
    }
}
