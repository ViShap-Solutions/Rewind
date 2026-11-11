namespace ViShap.Rewind.Tests.Nesting;

public sealed class NestingTests
{
    [Fact]
    public void NestedRun_InnerThrowsAndIsCaught_RollsBackInnerOnly()
    {
        var a = new RewindCell<int>(0);
        var b = new RewindCell<int>(0);

        Rewind.Run(() =>
        {
            a.Value = 1;
            try
            {
                Rewind.Run(() =>
                {
                    b.Value = 2;
                    throw new InvalidOperationException();
                });
            }
            catch (InvalidOperationException)
            {
            }
        });

        Assert.Equal(1, a.Value);
        Assert.Equal(0, b.Value);
    }

    [Fact]
    public void NestedRun_OuterThrowsAfterInnerCommitted_RollsBackBoth()
    {
        var a = new RewindCell<int>(0);
        var b = new RewindCell<int>(0);

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
        {
            a.Value = 1;
            Rewind.Run(() => { b.Value = 2; });
            throw new InvalidOperationException();
        }));

        Assert.Equal(0, a.Value);
        Assert.Equal(0, b.Value);
    }

    [Fact]
    public void NestedRun_SameCellWrittenInOuterAndInner_InnerRollbackRestoresOuterValue()
    {
        var cell = new RewindCell<int>(0);

        Rewind.Run(() =>
        {
            cell.Value = 1;
            Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
            {
                cell.Value = 2;
                throw new InvalidOperationException();
            }));
            Assert.Equal(1, cell.Value);
        });

        Assert.Equal(1, cell.Value);
    }

    [Fact]
    public void NestedRun_SameCellAfterInnerCommit_OuterRollbackRestoresStart()
    {
        var cell = new RewindCell<int>(0);

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
        {
            cell.Value = 1;
            Rewind.Run(() => { cell.Value = 2; });
            cell.Value = 3;
            throw new InvalidOperationException();
        }));

        Assert.Equal(0, cell.Value);
    }

    [Fact]
    public void Dispose_OuterWhileInnerOpen_ThrowsScopeAndChangesNothing()
    {
        var cell = new RewindCell<int>(0);
        var outer = Rewind.Begin();
        cell.Value = 1;
        var inner = Rewind.Begin();
        cell.Value = 2;

        Assert.Throws<RewindScopeException>(outer.Dispose);

        Assert.True(outer.IsActive);
        Assert.True(inner.IsActive);
        Assert.Same(inner, Rewind.Current);
        Assert.Equal(2, cell.Value);
        inner.Dispose();
        outer.Dispose();
        Assert.Equal(0, cell.Value);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void Commit_OuterWhileInnerOpen_ThrowsScopeAndChangesNothing()
    {
        var cell = new RewindCell<int>(0);
        var outer = Rewind.Begin();
        cell.Value = 1;
        var inner = Rewind.Begin();
        cell.Value = 2;

        Assert.Throws<RewindScopeException>(outer.Commit);

        Assert.True(outer.IsActive);
        Assert.True(inner.IsActive);
        Assert.Same(inner, Rewind.Current);
        Assert.Equal(2, cell.Value);
        inner.Dispose();
        outer.Dispose();
        Assert.Equal(0, cell.Value);
        Assert.Null(Rewind.Current);
    }
}
