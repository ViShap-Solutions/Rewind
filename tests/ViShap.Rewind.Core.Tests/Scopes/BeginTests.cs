namespace ViShap.Rewind.Tests.Scopes;

public sealed class BeginTests
{
    [Fact]
    public void Dispose_WithoutCommit_RestoresCell()
    {
        var cell = new RewindCell<int>(1);

        using (Rewind.Begin())
        {
            cell.Value = 2;
        }

        Assert.Equal(1, cell.Value);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void Commit_ThenDispose_KeepsWrites()
    {
        var cell = new RewindCell<int>(1);

        using (var scope = Rewind.Begin())
        {
            cell.Value = 2;
            scope.Commit();
        }

        Assert.Equal(2, cell.Value);
    }

    [Fact]
    public void Commit_Twice_ThrowsScope()
    {
        using var scope = Rewind.Begin();
        scope.Commit();

        Assert.Throws<RewindScopeException>(() => scope.Commit());
    }

    [Fact]
    public void Begin_SetsCurrentAndDepth()
    {
        using var outer = Rewind.Begin();
        using var inner = Rewind.Begin();

        Assert.Same(inner, Rewind.Current);
        Assert.Equal(0, outer.Depth);
        Assert.Equal(1, inner.Depth);

        inner.Commit();
        outer.Commit();
    }

    [Fact]
    public void Write_WithNoScope_IsPlainWrite()
    {
        var cell = new RewindCell<int>(1);

        cell.Value = 2;

        Assert.Null(Rewind.Current);
        Assert.Equal(2, cell.Value);
    }

    [Fact]
    public void Constructor_Default_HoldsDefaultValue()
    {
        var cell = new RewindCell<int>();

        Assert.Equal(0, cell.Value);
    }
}
