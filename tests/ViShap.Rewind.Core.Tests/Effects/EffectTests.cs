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

    [Fact]
    public void AfterCommit_InnerCommittedThenOuterRollsBack_IsDiscarded()
    {
        var log = new List<string>();

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
        {
            Rewind.Run(inner => inner.AfterCommit(() => log.Add("sent")));
            throw new InvalidOperationException();
        }));

        Assert.Empty(log);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void Effect_ScopeRollsBack_UndoesInterleavedWithCellsNewestFirst()
    {
        var a = new RewindCell<int>(0);
        var b = new RewindCell<int>(0);
        var log = new List<string>();

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(scope =>
        {
            a.Value = 1;
            scope.Effect(() => { }, () => log.Add($"undo 1: a={a.Value} b={b.Value}"));
            b.Value = 2;
            scope.Effect(() => { }, () => log.Add($"undo 2: a={a.Value} b={b.Value}"));
            throw new InvalidOperationException();
        }));

        Assert.Equal(new[] { "undo 2: a=1 b=2", "undo 1: a=1 b=0" }, log);
        Assert.Equal(0, a.Value);
        Assert.Equal(0, b.Value);
    }

    [Fact]
    public void Effect_UndoWritesCell_WriteIsPlain()
    {
        var cell = new RewindCell<int>(0);

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(scope =>
        {
            scope.Effect(() => { }, () => cell.Value = 5);
            throw new InvalidOperationException();
        }));

        Assert.Equal(5, cell.Value);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void AfterCommit_RegisteredAcrossTree_RunsInRegistrationOrder()
    {
        var log = new List<string>();

        Rewind.Run(outer =>
        {
            outer.AfterCommit(() => log.Add("outer 1"));
            Rewind.Run(inner =>
            {
                inner.AfterCommit(() => log.Add("inner 1"));
                Rewind.Run(deepest => deepest.AfterCommit(() => log.Add("deepest")));
                inner.AfterCommit(() => log.Add("inner 2"));
            });
            outer.AfterCommit(() => log.Add("outer 2"));
        });

        Assert.Equal(new[] { "outer 1", "inner 1", "deepest", "inner 2", "outer 2" }, log);
    }

    [Fact]
    public void AfterRollback_RegisteredInCommittedChild_RunsWhenOuterRollsBack()
    {
        var log = new List<string>();

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(outer =>
        {
            outer.AfterRollback(() => log.Add("outer"));
            Rewind.Run(inner => inner.AfterRollback(() => log.Add("inner")));
            throw new InvalidOperationException();
        }));

        Assert.Equal(new[] { "outer", "inner" }, log);
    }

    [Fact]
    public void AfterRollbackAndUndo_Run_WithNoCurrentScope()
    {
        var seen = new List<RewindScope?>();
        var probes = 0;

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(scope =>
        {
            scope.Effect(() => { }, () =>
            {
                seen.Add(Rewind.Current);
                probes++;
            });
            scope.AfterRollback(() =>
            {
                seen.Add(Rewind.Current);
                probes++;
            });
            throw new InvalidOperationException();
        }));

        Assert.Equal(2, probes);
        Assert.All(seen, Assert.Null);
    }

    [Fact]
    public void AfterRollback_EffectThrows_RunsOthersAndCarriesOriginal()
    {
        var log = new List<string>();
        var original = new InvalidOperationException("declined");
        var broken = new InvalidOperationException("reaction failed");

        var thrown = Assert.Throws<RewindCompensationException>(() => Rewind.Run(scope =>
        {
            scope.Effect(() => { }, () => log.Add("undo"));
            scope.AfterRollback(() => throw broken);
            scope.AfterRollback(() => log.Add("second reaction"));
            throw original;
        }));

        Assert.Same(original, thrown.OriginalException);
        Assert.Same(broken, Assert.Single(thrown.Failures));
        Assert.Equal(new[] { "undo", "second reaction" }, log);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void NestedScope_RollsBack_DiscardsItsDeferredRunsItsCompensationsAndReactionsKeepsParents()
    {
        var log = new List<string>();

        Rewind.Run(outer =>
        {
            outer.AfterCommit(() => log.Add("outer after commit"));
            outer.Effect(() => log.Add("outer do"), () => log.Add("outer undo"));
            Assert.Throws<InvalidOperationException>(() => Rewind.Run(inner =>
            {
                inner.AfterCommit(() => log.Add("inner after commit"));
                inner.Effect(() => log.Add("inner do"), () => log.Add("inner undo"));
                inner.AfterRollback(() => log.Add("inner after rollback"));
                throw new InvalidOperationException();
            }));
        });

        Assert.Equal(new[] { "outer do", "inner do", "inner undo", "inner after rollback", "outer after commit" }, log);
    }
}
