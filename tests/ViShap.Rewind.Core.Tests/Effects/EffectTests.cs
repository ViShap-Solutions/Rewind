namespace ViShap.Rewind.Tests.Effects;

public sealed class EffectTests
{
    [Fact]
    public void AfterCommit_InNestedScope_RunsOnlyAfterOutermostCommit()
    {
        var log = new List<string>();

        Rewind.Run(() =>
        {
            Rewind.Run(inner => inner.AfterCommit(() => log.Add("sent")));
            Assert.Empty(log);
        });

        Assert.Equal(new[] { "sent" }, log);
    }

    [Fact]
    public void AfterCommit_ScopeRollsBack_IsDiscarded()
    {
        var log = new List<string>();

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(scope =>
        {
            scope.AfterCommit(() => log.Add("sent"));
            throw new InvalidOperationException();
        }));

        Assert.Empty(log);
    }

    [Fact]
    public void AfterCommit_RunsWithNoCurrentScope()
    {
        RewindScope? seen = null;
        var probed = false;

        Rewind.Run(scope => scope.AfterCommit(() =>
        {
            seen = Rewind.Current;
            probed = true;
        }));

        Assert.True(probed);
        Assert.Null(seen);
    }

    [Fact]
    public void AfterRollback_ScopeRollsBack_RunsAfterStateRestored()
    {
        var cell = new RewindCell<int>(1);
        var observed = -1;

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(scope =>
        {
            cell.Value = 2;
            scope.AfterRollback(() => observed = cell.Value);
            throw new InvalidOperationException();
        }));

        Assert.Equal(1, observed);
    }

    [Fact]
    public void Effect_ScopeRollsBack_UndoesInReverseOrder()
    {
        var log = new List<string>();

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(scope =>
        {
            scope.Effect(() => log.Add("do 1"), () => log.Add("undo 1"));
            scope.Effect(() => log.Add("do 2"), () => log.Add("undo 2"));
            throw new InvalidOperationException();
        }));

        Assert.Equal(new[] { "do 1", "do 2", "undo 2", "undo 1" }, log);
    }

    [Fact]
    public void Effect_UndoThrows_RunsOtherUndosAndCarriesOriginal()
    {
        var log = new List<string>();
        var original = new InvalidOperationException("declined");
        var broken = new IOException("disk");

        var thrown = Assert.Throws<RewindCompensationException>(() => Rewind.Run(scope =>
        {
            scope.Effect(() => { }, () => log.Add("undo 1"));
            scope.Effect(() => { }, () => throw broken);
            throw original;
        }));

        Assert.Same(original, thrown.OriginalException);
        Assert.Same(broken, Assert.Single(thrown.Failures));
        Assert.Equal(new[] { "undo 1" }, log);
    }

    [Fact]
    public void Effect_DoThrows_UndoIsNotRegistered()
    {
        var undone = false;

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(scope =>
            scope.Effect(() => throw new InvalidOperationException(), () => undone = true)));

        Assert.False(undone);
    }

    [Fact]
    public void AfterCommit_EffectThrows_StateStaysCommittedAndOtherEffectsRun()
    {
        var cell = new RewindCell<int>(1);
        var log = new List<string>();

        var thrown = Assert.Throws<RewindEffectException>(() => Rewind.Run(scope =>
        {
            cell.Value = 2;
            scope.AfterCommit(() => throw new InvalidOperationException());
            scope.AfterCommit(() => log.Add("second"));
        }));

        Assert.Single(thrown.Failures);
        Assert.Equal(2, cell.Value);
        Assert.Equal(new[] { "second" }, log);
    }

    [Fact]
    public void AfterCommit_OnOuterScopeWhileInnerOpen_ThrowsScope()
    {
        using var outer = Rewind.Begin();
        using var inner = Rewind.Begin();

        Assert.Throws<RewindScopeException>(() => outer.AfterCommit(() => { }));
    }
}
