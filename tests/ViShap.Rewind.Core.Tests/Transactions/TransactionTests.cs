using System.Transactions;
using ViShap.Rewind.Engine;
using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Transactions;

public sealed class TransactionTests
{
    [Fact]
    public void Commit_InAmbientTransaction_DefersCompletionUntilTransactionCommits()
    {
        var cell = new RewindCell<int>(0);
        var document = new Document { Title = "Draft" };
        var notified = new NotificationLog(document);
        var log = new List<string>();

        using (var transaction = new TransactionScope())
        {
            Rewind.Run(scope =>
            {
                cell.Value = 1;
                document.Title = "Paid";
                scope.OnCommitted(_ => log.Add("observed"));
                scope.AfterCommit(() => log.Add("after commit"));
            });

            Assert.Equal(1, cell.Value);
            Assert.Empty(log);
            Assert.Empty(notified.Names);
            Assert.Null(Rewind.Current);
            transaction.Complete();
        }

        Assert.Equal(new[] { "observed", "after commit" }, log);
        Assert.Equal(new[] { "Title" }, notified.Names);
        Assert.Equal(1, cell.Value);
    }

    [Fact]
    public void Rollback_OfAmbientTransaction_UndoesScopeNotifiesWhatItRestoredAndRunsRollbackEffects()
    {
        var cell = new RewindCell<int>(0);
        var document = new Document { Title = "Draft" };
        var notified = new NotificationLog(document);
        var log = new List<string>();

        using (new TransactionScope())
        {
            Rewind.Run(scope =>
            {
                cell.Value = 1;
                document.Title = "Paid";
                scope.AfterCommit(() => log.Add("after commit"));
                scope.AfterRollback(() => log.Add("after rollback"));
            });
        }

        Assert.Equal(0, cell.Value);
        Assert.Equal("Draft", document.Title);
        Assert.Equal(new[] { "after rollback" }, log);
        Assert.Equal(new[] { "Title" }, notified.Names);
    }

    [Fact]
    public void Rollback_OfAmbientTransaction_UndoesSeveralScopesNewestFirst()
    {
        var cell = new RewindCell<int>(0);
        var list = new RewindList<int>();

        using (new TransactionScope())
        {
            Rewind.Run(() =>
            {
                cell.Value = 1;
                list.Add(1);
            });
            Rewind.Run(() =>
            {
                cell.Value = 2;
                list.Insert(0, 2);
            });
        }

        Assert.Equal(0, cell.Value);
        Assert.Empty(list);
    }

    [Fact]
    public void ScopeRollsBack_InAmbientTransaction_RollsTransactionBack()
    {
        var earlier = new RewindCell<int>(0);
        var failing = new RewindCell<int>(0);
        var transaction = new TransactionScope();
        Rewind.Run(() => earlier.Value = 5);

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
        {
            failing.Value = 1;
            throw new InvalidOperationException();
        }));
        transaction.Complete();

        Assert.Throws<TransactionAbortedException>(transaction.Dispose);
        Assert.Equal(0, earlier.Value);
        Assert.Equal(0, failing.Value);
    }

    [Fact]
    public void NestedScopes_InAmbientTransaction_OnlyOutermostJoins()
    {
        var a = new RewindCell<int>(0);
        var b = new RewindCell<int>(0);
        var c = new RewindCell<int>(0);

        using (var transaction = new TransactionScope())
        {
            Rewind.Run(() =>
            {
                a.Value = 1;
                Rewind.Run(() => b.Value = 2);
                Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
                {
                    c.Value = 3;
                    throw new InvalidOperationException();
                }));
            });
            transaction.Complete();
        }

        Assert.Equal((1, 2, 0), (a.Value, b.Value, c.Value));
    }

    [Fact]
    public void Commit_WithoutJoinedTransaction_CompletesAtOnce()
    {
        var log = new List<string>();
        var cell = new RewindCell<int>(0);

        Rewind.Run(scope => scope.AfterCommit(() => log.Add("no transaction")));
        using (new TransactionScope())
        {
            using (new TransactionScope(TransactionScopeOption.Suppress))
            {
                Rewind.Run(scope => scope.AfterCommit(() => log.Add("suppressed")));
            }
        }

        var before = Rewind.Begin();
        using (new TransactionScope())
        {
            cell.Value = 1;
        }

        before.AfterCommit(() => log.Add("opened before"));
        before.Commit();
        before.Dispose();

        Assert.Equal(new[] { "no transaction", "suppressed", "opened before" }, log);
        Assert.Equal(1, cell.Value);
    }

    [Fact]
    public void TransactionCommit_EffectThrows_DisposeThrowsEffectExceptionAndStateStays()
    {
        var cell = new RewindCell<int>(0);
        var transaction = new TransactionScope();
        Rewind.Run(scope =>
        {
            cell.Value = 1;
            scope.AfterCommit(() => throw new InvalidOperationException("mail"));
        });
        transaction.Complete();

        var thrown = Assert.Throws<RewindEffectException>(transaction.Dispose);

        Assert.Equal("mail", Assert.Single(thrown.Failures).Message);
        Assert.Equal(1, cell.Value);
    }

    [Fact]
    public void TransactionRollback_CompensationThrows_DisposeThrowsCompensationExceptionAndStateRestored()
    {
        var cell = new RewindCell<int>(0);
        var transaction = new TransactionScope();
        Rewind.Run(scope =>
        {
            cell.Value = 1;
            scope.Effect(() => { }, () => throw new InvalidOperationException("release"));
        });

        var thrown = Assert.Throws<RewindCompensationException>(transaction.Dispose);

        Assert.Null(thrown.OriginalException);
        Assert.Equal("release", Assert.Single(thrown.Failures).Message);
        Assert.Equal(0, cell.Value);
    }

    [Fact]
    public void InDoubt_KeepsStateRunsNotificationsAndObserversButNoEffects()
    {
        var document = new Document { Title = "Draft" };
        var notified = new NotificationLog(document);
        var log = new List<string>();
        List<Exception>? failures;

        using (new TransactionScope())
        {
            Rewind.Run(scope =>
            {
                document.Title = "Paid";
                scope.OnCommitted(_ => log.Add("observed"));
                scope.AfterCommit(() => log.Add("after commit"));
                scope.AfterRollback(() => log.Add("after rollback"));
            });
            failures = TransactionParticipant.Of(Transaction.Current!)!.InDoubt();
        }

        Assert.Null(failures);
        Assert.Equal("Paid", document.Title);
        Assert.Equal(new[] { "Title" }, notified.Names);
        Assert.Equal(new[] { "observed" }, log);
    }

    [Fact]
    public void Commit_AfterTransactionAborted_RollsScopeBackAndThrowsScope()
    {
        var cell = new RewindCell<int>(0);
        var log = new List<string>();
        var transaction = new TransactionScope();
        var scope = Rewind.Begin();
        cell.Value = 1;
        scope.AfterRollback(() => log.Add("after rollback"));
        Transaction.Current!.Rollback();

        Assert.Throws<RewindScopeException>(scope.Commit);

        Assert.Equal(0, cell.Value);
        Assert.False(scope.IsActive);
        Assert.Null(Rewind.Current);
        Assert.Equal(new[] { "after rollback" }, log);
        scope.Dispose();
        transaction.Dispose();
    }

    [Fact]
    public void Commit_AfterTransactionCommitted_CompletesAtOnce()
    {
        var cell = new RewindCell<int>(0);
        var log = new List<string>();
        RewindScope scope;

        using (var transaction = new TransactionScope())
        {
            scope = Rewind.Begin();
            cell.Value = 1;
            transaction.Complete();
        }

        scope.AfterCommit(() => log.Add("after commit"));
        scope.Commit();
        scope.Dispose();

        Assert.Equal(1, cell.Value);
        Assert.Equal(new[] { "after commit" }, log);
    }

    [Fact]
    public void JoinedScope_SingleResourceManager_StillCommitsInOnePhase()
    {
        var cell = new RewindCell<int>(0);
        var database = new SinglePhaseResourceManager();

        using (var transaction = new TransactionScope())
        {
            Transaction.Current!.EnlistVolatile(database, EnlistmentOptions.None);
            Rewind.Run(() => cell.Value = 1);
            transaction.Complete();
        }

        Assert.Equal(new[] { "single-phase commit" }, database.Calls);
        Assert.Equal(1, cell.Value);
    }

    [Fact]
    public void JoinedScope_SingleResourceManagerRefusesItsCommit_StateRestored()
    {
        var cell = new RewindCell<int>(0);
        var database = new SinglePhaseResourceManager { Refuse = true };

        Assert.Throws<TransactionAbortedException>(() =>
        {
            using var transaction = new TransactionScope();
            Transaction.Current!.EnlistVolatile(database, EnlistmentOptions.None);
            Rewind.Run(() => cell.Value = 1);
            transaction.Complete();
        });

        Assert.Equal(new[] { "single-phase commit" }, database.Calls);
        Assert.Equal(0, cell.Value);
    }

    /// <summary>
    /// A resource manager that commits in one phase when it is the only participant and refuses a prepare, as a
    /// PostgreSQL connection does when prepared transactions are disabled.
    /// </summary>
    private sealed class SinglePhaseResourceManager : ISinglePhaseNotification
    {
        public List<string> Calls { get; } = [];

        public bool Refuse { get; init; }

        public void SinglePhaseCommit(SinglePhaseEnlistment singlePhaseEnlistment)
        {
            Calls.Add("single-phase commit");

            if (Refuse)
            {
                singlePhaseEnlistment.Aborted(new InvalidOperationException("refused"));
                return;
            }

            singlePhaseEnlistment.Committed();
        }

        public void Prepare(PreparingEnlistment preparingEnlistment)
        {
            Calls.Add("prepare");
            preparingEnlistment.ForceRollback(new InvalidOperationException("prepared transactions are disabled"));
        }

        public void Commit(Enlistment enlistment)
        {
            Calls.Add("commit");
            enlistment.Done();
        }

        public void Rollback(Enlistment enlistment)
        {
            Calls.Add("rollback");
            enlistment.Done();
        }

        public void InDoubt(Enlistment enlistment)
        {
            Calls.Add("in doubt");
            enlistment.Done();
        }
    }
}
