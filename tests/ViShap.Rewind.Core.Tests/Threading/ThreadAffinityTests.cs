namespace ViShap.Rewind.Tests.Threading;

public sealed class ThreadAffinityTests
{
    [Fact]
    public void Commit_FromAnotherThread_ThrowsScopeAndChangesNothing()
    {
        var cell = new RewindCell<int>(1);
        var scope = Rewind.Begin();
        cell.Value = 2;

        var thrown = OnOtherThread(() => Record.Exception(() => scope.Commit()));

        Assert.IsType<RewindScopeException>(thrown);
        Assert.True(scope.IsActive);
        scope.Dispose();
        Assert.Equal(1, cell.Value);
    }

    [Fact]
    public void Scope_IsNotVisibleOnAnotherThread()
    {
        using var scope = Rewind.Begin();

        var seen = OnOtherThread(() => Rewind.Current);

        Assert.Null(seen);
        scope.Commit();
    }

    [Fact]
    public void Write_OnAnotherThreadWhileScopeOpen_IsNotRecordedByThatScope()
    {
        var cell = new RewindCell<int>(1);

        using (Rewind.Begin())
        {
            OnOtherThread(() => cell.Value = 2);
        }

        Assert.Equal(2, cell.Value);
    }

    private static T OnOtherThread<T>(Func<T> func)
    {
        T result = default!;
        var thread = new Thread(() => result = func());
        thread.Start();
        thread.Join();
        return result;
    }
}
