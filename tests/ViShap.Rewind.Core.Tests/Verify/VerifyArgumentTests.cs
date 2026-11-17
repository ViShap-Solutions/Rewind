using ViShap.Rewind.Engine;
using ViShap.Rewind.Testing;

namespace ViShap.Rewind.Tests.Verify;

public sealed class VerifyArgumentTests
{
    [Fact]
    public void Verify_InvalidArguments_Refused()
    {
        var ran = false;
        Action operation = () => ran = true;
        Func<Task> asynchronous = () =>
        {
            ran = true;
            return Task.CompletedTask;
        };
        Func<int> retried = () => ran ? 1 : 0;
        Func<Task<int>> retriedAsynchronously = () => Task.FromResult(ran ? 1 : 0);

        Assert.Throws<ArgumentNullException>(() => RewindVerify.Rollback(null!, () => 0));
        Assert.Throws<ArgumentNullException>(() => RewindVerify.Rollback<int>(operation, null!));
        Assert.Throws<ArgumentNullException>(() => RewindVerify.Rollback(operation, () => 0, null!));
        Assert.Throws<ArgumentNullException>(() => { _ = RewindVerify.RollbackAsync(null!, () => 0); });
        Assert.Throws<ArgumentNullException>(() => { _ = RewindVerify.RollbackAsync(asynchronous, () => 0, null!); });
        Assert.Throws<ArgumentNullException>(() => RewindVerify.Retries<int, int>(null!, 2, () => 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RewindVerify.Retries(retried, 1, () => 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = RewindVerify.RetriesAsync(retriedAsynchronously, 1, () => 0); });
        using (Rewind.Begin())
        {
            Assert.Throws<RewindScopeException>(() => RewindVerify.Rollback(operation, () => 0));
            Assert.Throws<RewindScopeException>(() => { _ = RewindVerify.RetriesAsync(retriedAsynchronously, 2, () => 0); });
        }

        Assert.False(ran);
    }

    [Fact]
    public void Hook_OutsideArmedFlow_LeavesJournalPlain()
    {
        var cell = new RewindCell<int>(0);
        var armed = new List<Type>();
        var unrelated = new List<Type>();

        RewindVerify.Rollback(
            () => Rewind.Run(() =>
            {
                cell.Value = 1;
                armed.Add(Rewind.Current!.Journal.GetType());
                var thread = new Thread(() =>
                {
                    using var scope = Rewind.Begin();
                    lock (unrelated)
                    {
                        unrelated.Add(scope.Journal.GetType());
                    }
                });
                using (ExecutionContext.SuppressFlow())
                {
                    thread.Start();
                }

                thread.Join();
            }),
            () => cell.Value);

        using var after = Rewind.Begin();
        Assert.All(armed, type => Assert.Equal(typeof(InjectingJournal), type));
        Assert.NotEmpty(unrelated);
        Assert.All(unrelated, type => Assert.Equal(typeof(Journal), type));
        Assert.Equal(typeof(Journal), after.Journal.GetType());
    }
}
