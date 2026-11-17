using ViShap.Rewind.Testing;
using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Verify;

public sealed class RollbackTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Rollback_AtomicOperation_Passes()
    {
        var order = new Order { Status = "new", Total = 0m };
        var lines = new RewindList<string>();
        var cell = new RewindCell<int>(0);
        var holds = new List<string>();
        var path = _directory.File("order.txt");
        File.WriteAllText(path, "new");

        RewindVerify.Rollback(
            () => Rewind.Run(scope =>
            {
                order.Status = "paid";
                Rewind.Run(() =>
                {
                    order.Total = 10m;
                    lines.Add("a");
                });
                cell.Value = 1;
                scope.Effect(() => holds.Add("hold"), () => holds.Remove("hold"));
                RewindFile.WriteAllText(path, "paid");
                scope.AfterCommit(() => holds.Add("receipt"));
                scope.AfterRollback(() => { });
            }),
            () => (order.Status, order.Total, string.Join(",", lines), cell.Value, string.Join(",", holds), File.ReadAllText(path)));

        Assert.Equal("new", order.Status);
        Assert.Empty(lines);
        Assert.Empty(holds);
        Assert.Equal("new", File.ReadAllText(path));
    }

    [Fact]
    public void Rollback_UntrackedWrite_ReportedByCountingRun()
    {
        var cell = new RewindCell<int>(0);
        var untracked = 0;

        var exception = Assert.Throws<RewindVerificationException>(() => RewindVerify.Rollback(
            () => Rewind.Run(() =>
            {
                cell.Value = 1;
                untracked++;
            }),
            () => (cell.Value, untracked)));

        Assert.Equal(0, exception.FailurePoint);
        Assert.Equal(1, exception.FailurePointCount);
        Assert.Null(exception.Point);
        Assert.Contains("Expected: (0, 0)", exception.Message);
        Assert.Contains("Actual:   (0, 1)", exception.Message);
    }

    [Fact]
    public void Rollback_TwoScopesFirstCommits_ReportedAtPointOfSecond()
    {
        var order = new Order { Status = "new", Total = 0m };

        var exception = Assert.Throws<RewindVerificationException>(() => RewindVerify.Rollback(
            () =>
            {
                Rewind.Run(() => order.Total = 10m);
                Rewind.Run(() => order.Status = "paid");
            },
            () => (order.Status, order.Total)));

        Assert.Equal(2, exception.FailurePoint);
        Assert.Equal(2, exception.FailurePointCount);
        Assert.Equal("write to Order.Status", exception.Point);
        Assert.IsType<RewindVerificationException>(exception);
        Assert.Contains("not as it was", exception.Message);
    }

    [Fact]
    public void Rollback_DeferredEffectRanBeforeFailure_Reported()
    {
        var cell = new RewindCell<int>(0);
        var sent = new List<string>();

        var exception = Assert.Throws<RewindVerificationException>(() => RewindVerify.Rollback(
            () =>
            {
                Rewind.Run(scope => scope.AfterCommit(() => sent.Add("mail")));
                Rewind.Run(() => cell.Value = 1);
            },
            () => cell.Value));

        Assert.Equal(2, exception.FailurePoint);
        Assert.Equal("write to RewindCell<Int32>", exception.Point);
        Assert.Contains("deferred effect", exception.Message);
        Assert.Equal(new[] { "mail" }, sent);
    }

    [Fact]
    public void Rollback_CaughtFailureStateRestored_Passes()
    {
        var cell = new RewindCell<int>(0);
        var list = new RewindList<string>();

        RewindVerify.Rollback(
            () =>
            {
                try
                {
                    Rewind.Run(() =>
                    {
                        cell.Value = 1;
                        list.Add("x");
                    });
                }
                catch (Exception)
                {
                }
            },
            () => (cell.Value, list.Count));
    }

    [Fact]
    public void Rollback_CaughtFailureStateChanged_Reported()
    {
        var cell = new RewindCell<int>(0);
        var note = "none";

        var exception = Assert.Throws<RewindVerificationException>(() => RewindVerify.Rollback(
            () =>
            {
                try
                {
                    Rewind.Run(() => cell.Value = 1);
                }
                catch (Exception)
                {
                    note = "failed";
                }
            },
            () => (cell.Value, note)));

        Assert.Equal(1, exception.FailurePoint);
        Assert.Contains("caught the failure", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Rollback_OperationWithoutScope_Reported()
    {
        var cell = new RewindCell<int>(0);

        var exception = Assert.Throws<RewindVerificationException>(() => RewindVerify.Rollback(() => cell.Value = 1, () => 0));

        Assert.Equal(1, exception.FailurePoint);
        Assert.Contains("no scope of its own", exception.Message);
    }

    [Fact]
    public void Rollback_NondeterministicOperation_Reported()
    {
        var first = new RewindCell<int>(0);
        var second = new RewindCell<int>(0);
        var calls = 0;

        var exception = Assert.Throws<RewindVerificationException>(() => RewindVerify.Rollback(
            () => Rewind.Run(() =>
            {
                if (calls++ == 0)
                {
                    first.Value = 1;
                }

                second.Value = 1;
            }),
            () => (first.Value, second.Value)));

        Assert.Equal(2, exception.FailurePoint);
        Assert.Contains("passed 1 of 2", exception.Message);
    }

    [Fact]
    public void Rollback_OperationFailsOnItsOwn_ReportedWithInner()
    {
        var thrown = new InvalidOperationException();

        var exception = Assert.Throws<RewindVerificationException>(() => RewindVerify.Rollback(() => Rewind.Run(() => throw thrown), () => 0));

        Assert.Same(thrown, exception.InnerException);
        Assert.Equal(0, exception.FailurePoint);
    }

    [Fact]
    public void Rollback_WithComparer_UsesIt()
    {
        var cell = new RewindCell<int>(0);
        var name = "abc";
        Action operation = () => Rewind.Run(() =>
        {
            cell.Value = 1;
            name = name == "abc" ? "ABC" : "abc";
        });

        RewindVerify.Rollback(operation, () => name + cell.Value, StringComparer.OrdinalIgnoreCase);
        Assert.Throws<RewindVerificationException>(() => RewindVerify.Rollback(operation, () => name + cell.Value));
    }
}
