using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Threading;

public sealed class ThreadAffinityTests
{
    [Fact]
    public void Commit_FromAnotherThread_ThrowsScopeAndChangesNothing()
    {
        var cell = new RewindCell<int>(1);
        var scope = Rewind.Begin();
        cell.Value = 2;

        var thrown = OtherThread.Capture(scope.Commit);

        Assert.IsType<RewindScopeException>(thrown);
        Assert.True(scope.IsActive);
        scope.Dispose();
        Assert.Equal(1, cell.Value);
    }

    [Fact]
    public void Scope_IsNotVisibleOnAnotherThread()
    {
        using var scope = Rewind.Begin();

        var seen = OtherThread.Run(() => Rewind.Current);

        Assert.Null(seen);
        scope.Commit();
    }

    [Fact]
    public void Write_OnAnotherThreadWhileScopeOpen_IsNotRecordedByThatScope()
    {
        var cell = new RewindCell<int>(1);

        using (Rewind.Begin())
        {
            OtherThread.Run(() => cell.Value = 2);
        }

        Assert.Equal(2, cell.Value);
    }

    [Fact]
    public void Register_FromAnotherThread_ThrowsScopeAndRegistersNothing()
    {
        var log = new List<string>();
        var scope = Rewind.Begin();

        var afterCommit = OtherThread.Capture(() => scope.AfterCommit(() => log.Add("after commit")));
        var afterRollback = OtherThread.Capture(() => scope.AfterRollback(() => log.Add("after rollback")));
        var effect = OtherThread.Capture(() => scope.Effect(() => log.Add("do"), () => log.Add("undo")));

        Assert.IsType<RewindScopeException>(afterCommit);
        Assert.IsType<RewindScopeException>(afterRollback);
        Assert.IsType<RewindScopeException>(effect);
        scope.Dispose();
        Assert.Empty(log);
    }

    [Fact]
    public void Dispose_OpenScopeFromAnotherThread_ThrowsScopeAndChangesNothing()
    {
        var cell = new RewindCell<int>(1);
        var scope = Rewind.Begin();
        cell.Value = 2;

        var thrown = OtherThread.Capture(scope.Dispose);

        Assert.IsType<RewindScopeException>(thrown);
        Assert.True(scope.IsActive);
        Assert.Equal(2, cell.Value);
        Assert.Same(scope, Rewind.Current);
        scope.Dispose();
        Assert.Equal(1, cell.Value);
    }

    [Fact]
    public void Dispose_ClosedScopeFromAnotherThread_DoesNothing()
    {
        var cell = new RewindCell<int>(1);
        var scope = Rewind.Begin();
        cell.Value = 2;
        scope.Commit();

        var thrown = OtherThread.Capture(scope.Dispose);

        Assert.Null(thrown);
        Assert.Equal(2, cell.Value);
        Assert.Null(Rewind.Current);
    }
}
