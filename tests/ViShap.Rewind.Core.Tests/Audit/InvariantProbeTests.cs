using ViShap.Rewind.Tests.Fixtures;
using ViShap.Rewind.Tests.Verify;

namespace ViShap.Rewind.Tests.Audit;

/// <summary>Audit probes: each tries to break an invariant of contract §17 with a combination the stage tests do not use.</summary>
public sealed class InvariantProbeTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Rollback_EveryKindOfTrackedStateInNestedScopes_RestoresAllNewestFirst()
    {
        var order = new Order { Status = "new", Total = 1m };
        var cell = new RewindCell<int>(1);
        var list = new RewindList<int> { 1, 2, 3 };
        var dictionary = new RewindDictionary<string, int> { ["a"] = 1 };
        var set = new RewindSet<int> { 1 };
        var path = _directory.File("state.txt");
        File.WriteAllText(path, "start");
        var compensations = new List<string>();

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(scope =>
        {
            order.Status = "a";
            list.Clear();
            Rewind.Run(inner =>
            {
                order.Status = "b";
                cell.Value = 2;
                list.Add(9);
                dictionary.Remove("a");
                dictionary["b"] = 2;
                set.SymmetricExceptWith([1, 2]);
                RewindFile.WriteAllText(path, "inner");
                inner.Effect(() => { }, () => compensations.Add("inner"));
            });
            scope.Effect(() => { }, () => compensations.Add("outer"));
            order.Total = 5m;
            RewindFile.Delete(path);
            throw new InvalidOperationException();
        }));

        Assert.Equal(("new", 1m, 1), (order.Status, order.Total, cell.Value));
        Assert.Equal(new[] { 1, 2, 3 }, list);
        Assert.Equal(new[] { "a" }, dictionary.Keys);
        Assert.Equal(new[] { 1 }, set);
        Assert.Equal("start", File.ReadAllText(path));
        Assert.Equal(new[] { "outer", "inner" }, compensations);
        Assert.Empty(_directory.SideFiles());
    }

    [Fact]
    public void AfterCommit_OpensScopeOfItsOwn_ThatScopeCommitsAndRollsBackNormally()
    {
        var cell = new RewindCell<int>(0);
        var log = new List<string>();

        Rewind.Run(scope =>
        {
            cell.Value = 1;
            scope.AfterCommit(() =>
            {
                log.Add($"current {Rewind.Current is null}");
                Rewind.Run(inner =>
                {
                    cell.Value = 2;
                    inner.AfterCommit(() => log.Add($"nested effect {cell.Value}"));
                });
                Assert.Throws<ArgumentException>(() => Rewind.Run(() =>
                {
                    cell.Value = 3;
                    throw new ArgumentException();
                }));
            });
        });

        Assert.Equal(2, cell.Value);
        Assert.Equal(new[] { "current True", "nested effect 2" }, log);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void AfterRollback_OpensAndLeaksScope_NothingHangsAndCallerSeesNoScope()
    {
        var cell = new RewindCell<int>(0);
        RewindScope? leaked = null;

        var outcome = OtherThread.Run(() => Record.Exception(() => Rewind.Run(scope =>
        {
            cell.Value = 1;
            scope.AfterRollback(() =>
            {
#pragma warning disable RWD1001 // The probe leaks a scope from a rollback effect on purpose.
                leaked = Rewind.Begin();
#pragma warning restore RWD1001
                cell.Value = 7;
            });
            throw new InvalidOperationException();
        })));

        Assert.IsType<InvalidOperationException>(outcome);
        Assert.NotNull(leaked);
        Assert.True(leaked!.IsActive);
    }

    [Fact]
    public void Run_BlockLeaksScopeAndRollbackEffectLeaksAnother_UnwindEnds()
    {
        var cell = new RewindCell<int>(0);

        var outcome = OtherThread.Run(() => Record.Exception(() => Rewind.Run(scope =>
        {
            scope.AfterRollback(() =>
            {
#pragma warning disable RWD1001 // The probe leaks scopes on purpose.
                _ = Rewind.Begin();
#pragma warning restore RWD1001
            });
            cell.Value = 1;
#pragma warning disable RWD1001
            _ = Rewind.Begin();
            _ = Rewind.Begin();
#pragma warning restore RWD1001
            cell.Value = 2;
        })));

        Assert.IsType<RewindScopeException>(outcome);
        Assert.Equal(0, cell.Value);
    }

    [Fact]
    public void Rollback_CompensationsAndRollbackEffectsThrow_AllRunAndOriginalIsSameInstance()
    {
        var thrown = new InvalidOperationException();
        var ran = new List<string>();

        var exception = Assert.Throws<RewindCompensationException>(() => Rewind.Run(scope =>
        {
            scope.Effect(() => { }, () => { ran.Add("first"); throw new ArgumentException("first"); });
            Rewind.Run(inner => inner.Effect(() => { }, () => { ran.Add("nested"); throw new ArgumentException("nested"); }));
            scope.Effect(() => { }, () => ran.Add("last"));
            scope.AfterRollback(() => { ran.Add("after"); throw new ArgumentException("after"); });
            throw thrown;
        }));

        Assert.Same(thrown, exception.OriginalException);
        Assert.Equal(new[] { "last", "nested", "first", "after" }, ran);
        Assert.Equal(new[] { "nested", "first", "after" }, exception.Failures.Select(failure => failure.Message));
    }

    [Fact]
    public void Dispose_CompensationThrows_ThrowsCompensationWithNoOriginalAndRestoresState()
    {
        var cell = new RewindCell<int>(0);
        var scope = Rewind.Begin();
        cell.Value = 1;
        scope.Effect(() => { }, () => throw new ArgumentException());

        var exception = Assert.Throws<RewindCompensationException>(scope.Dispose);

        Assert.Null(exception.OriginalException);
        Assert.Equal(0, cell.Value);
        Assert.False(scope.IsActive);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void DeferredEffect_RegisteredInCommittedChildOfRolledBackParent_NeverRuns()
    {
        var ran = false;

        using (Rewind.Begin())
        {
            Rewind.Run(scope => scope.AfterCommit(() => ran = true));
            Rewind.Run(scope => Rewind.Run(inner => inner.AfterCommit(() => ran = true)));
        }

        Assert.False(ran);
    }

    [Fact]
    public void Dispose_OuterWhileInnerOpen_RefusedAndNothingChanged()
    {
        var cell = new RewindCell<int>(0);
        var outer = Rewind.Begin();
        cell.Value = 1;
        var inner = Rewind.Begin();
        cell.Value = 2;

        Assert.Throws<RewindScopeException>(outer.Dispose);
        Assert.Throws<RewindScopeException>(outer.Commit);
        Assert.Throws<RewindScopeException>(() => outer.AfterCommit(() => { }));

        Assert.Same(inner, Rewind.Current);
        Assert.Equal(2, cell.Value);
        inner.Dispose();
        outer.Dispose();
        Assert.Equal(0, cell.Value);
    }

    [Fact]
    public void Run_ValueTaskResult_RefusedBeforeRunning()
    {
        var ran = false;

        Assert.Throws<RewindScopeException>(() => Rewind.Run(() =>
        {
            ran = true;
            return new ValueTask<int>(1);
        }));

        Assert.False(ran);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void ScopeProperties_ReadFromAnotherThread_DoNotThrow()
    {
        using var scope = Rewind.Begin();

        var read = OtherThread.Capture(() => _ = (scope.IsActive, scope.Depth));

        Assert.Null(read);
    }
}
