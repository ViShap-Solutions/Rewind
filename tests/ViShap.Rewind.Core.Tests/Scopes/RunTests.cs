namespace ViShap.Rewind.Tests.Scopes;

public sealed class RunTests
{
    [Fact]
    public void Run_BlockCompletes_KeepsWrites()
    {
        var cell = new RewindCell<int>(1);

        Rewind.Run(() => { cell.Value = 2; });

        Assert.Equal(2, cell.Value);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void Run_BlockThrows_RestoresCellAndRethrowsOriginal()
    {
        var cell = new RewindCell<int>(1);
        var original = new InvalidOperationException("declined");

        var thrown = Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
        {
            cell.Value = 2;
            throw original;
        }));

        Assert.Same(original, thrown);
        Assert.Equal(1, cell.Value);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void Run_SeveralWritesToOneCell_RestoresValueAtScopeStart()
    {
        var cell = new RewindCell<string>("a");

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
        {
            cell.Value = "b";
            cell.Value = "c";
            cell.Value = "d";
            throw new InvalidOperationException();
        }));

        Assert.Equal("a", cell.Value);
    }

    [Fact]
    public void Run_WithResult_ReturnsResultAfterCommit()
    {
        var cell = new RewindCell<int>(1);

        var result = Rewind.Run(() => cell.Value = 5);

        Assert.Equal(5, result);
        Assert.Equal(5, cell.Value);
    }

    [Fact]
    public void Run_BlockReturningTask_ThrowsScopeBeforeRunning()
    {
        var ran = false;

        Assert.Throws<RewindScopeException>(RunBlockReturningTask);

        Assert.False(ran);
        Assert.Null(Rewind.Current);

        void RunBlockReturningTask() => Rewind.Run(() =>
        {
            ran = true;
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Run_BlockReturningValueTask_ThrowsScopeBeforeRunning()
    {
        Assert.Throws<RewindScopeException>(() => { Rewind.Run(() => ValueTask.FromResult(1)); });
    }

    [Fact]
    public void Run_StructOperationThrows_RestoresCell()
    {
        var cell = new RewindCell<int>(1);

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(new FailingWrite(cell, 9)));

        Assert.Equal(1, cell.Value);
    }

    [Fact]
    public void Run_WithState_PassesStateToBlock()
    {
        var cell = new RewindCell<int>(1);

        Rewind.Run(cell, static c => c.Value = 3);

        Assert.Equal(3, cell.Value);
    }

    [Fact]
    public void Run_CallbackCommitsItsScope_ThrowsScopeAndRollsBack()
    {
        var cell = new RewindCell<int>(1);

        Assert.Throws<RewindScopeException>(() => Rewind.Run(scope =>
        {
            cell.Value = 2;
            scope.Commit();
        }));

        Assert.Equal(1, cell.Value);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void Run_CallbackLeavesScopeOpen_ThrowsScopeAndRollsBackEverything()
    {
        var cell = new RewindCell<int>(1);

        Assert.Throws<RewindScopeException>(() => Rewind.Run(() =>
        {
            cell.Value = 2;
            Rewind.Begin();
            cell.Value = 3;
        }));

        Assert.Equal(1, cell.Value);
        Assert.Null(Rewind.Current);
    }

    private readonly struct FailingWrite(RewindCell<int> cell, int value) : IRewindOperation
    {
        public void Execute()
        {
            cell.Value = value;
            throw new InvalidOperationException();
        }
    }
}
