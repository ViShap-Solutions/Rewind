using System.Transactions;
using ViShap.Rewind.Integration.Tests.Fixtures;

namespace ViShap.Rewind.Integration.Tests.Databases;

/// <summary>
/// The scenarios of the release checklist against a real database: a Rewind scope and SQL work in one ambient
/// <see cref="TransactionScope"/>. Memory must end exactly as the database does — committed together, rolled back
/// together — and effects bound to the commit must run only once the database has committed.
/// </summary>
public abstract class TransactionScenarios(DatabaseFixture db, ITestOutputHelper output)
{
    protected DatabaseFixture Db { get; } = db;

    protected ITestOutputHelper Output { get; } = output;

    [Fact]
    public void Completed_DatabaseAndMemoryCommitTogether_AfterCommitSeesTheCommittedRow()
    {
        var key = Db.NextKey();
        var order = new Order { Status = "new" };
        var rowsSeenByEffect = -1;

        using (var transaction = new TransactionScope())
        {
            Rewind.Run(scope =>
            {
                order.Status = "paid";
                Db.Insert(key, "paid");
                scope.AfterCommit(() => rowsSeenByEffect = Db.Count(key));
            });

            Assert.Equal(-1, rowsSeenByEffect);
            transaction.Complete();
        }

        Assert.Equal("paid", order.Status);
        Assert.Equal(1, Db.Count(key));
        Assert.Equal(1, rowsSeenByEffect);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void NotCompleted_DatabaseAndMemoryRollBack_OnlyAfterRollbackRuns()
    {
        var key = Db.NextKey();
        var order = new Order { Status = "new" };
        var committed = false;
        var rolledBack = false;

        using (new TransactionScope())
        {
            Rewind.Run(scope =>
            {
                order.Status = "paid";
                order.Lines.Add("pen");
                Db.Insert(key, "paid");
                scope.AfterCommit(() => committed = true);
                scope.AfterRollback(() => rolledBack = true);
            });
        }

        Assert.Equal("new", order.Status);
        Assert.Empty(order.Lines);
        Assert.Equal(0, Db.Count(key));
        Assert.False(committed);
        Assert.True(rolledBack);
    }

    [Fact]
    public void BlockThrows_DatabaseAndMemoryRollBack_ExceptionReachesCaller()
    {
        var key = Db.NextKey();
        var order = new Order { Status = "new" };

        var thrown = Record.Exception(() =>
        {
            using var transaction = new TransactionScope();
            Rewind.Run(() =>
            {
                order.Status = "paid";
                Db.Insert(key, "paid");
                throw new InvalidOperationException("payment declined");
            });
            transaction.Complete();
        });

        Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal("new", order.Status);
        Assert.Equal(0, Db.Count(key));
    }

    [Fact]
    public void DuplicateKeyInsideTheBlock_DatabaseAndMemoryRollBack_ProviderErrorReachesCaller()
    {
        var existing = Db.NextKey();
        var fresh = Db.NextKey();
        Db.Insert(existing, "already there");
        var order = new Order { Status = "new" };

        var thrown = Record.Exception(() =>
        {
            using var transaction = new TransactionScope();
            Rewind.Run(() =>
            {
                order.Status = "paid";
                Db.Insert(fresh, "paid");
                Db.Insert(existing, "duplicate");
            });
            transaction.Complete();
        });

        Output.WriteLine($"{Db.Provider}: {thrown?.GetType().Name}: {thrown?.Message}");
        Assert.True(Db.IsDuplicateKey(thrown!), $"expected the provider's duplicate-key error, got {thrown}");
        Assert.Equal("new", order.Status);
        Assert.Equal(0, Db.Count(fresh));
        Assert.Equal(1, Db.Count(existing));
    }

    [Fact]
    public void ScopeRollsBackInsideCompletedTransaction_TransactionIsAborted()
    {
        var key = Db.NextKey();
        var order = new Order { Status = "new" };

        var thrown = Record.Exception(() =>
        {
            using var transaction = new TransactionScope();
            Db.Insert(key, "written before the scope");

            using (Rewind.Begin())
            {
                order.Status = "paid";
            }

            transaction.Complete();
        });

        Output.WriteLine($"{Db.Provider}: {thrown?.GetType().Name}: {thrown?.Message}");
        Assert.IsType<TransactionAbortedException>(thrown);
        Assert.Equal("new", order.Status);
        Assert.Equal(0, Db.Count(key));
    }

    [Fact]
    public async Task RunAsync_WithAsyncFlow_CommitsAndRollsBackWithTheDatabase()
    {
        var committedKey = Db.NextKey();
        var rolledBackKey = Db.NextKey();
        var order = new Order { Status = "new" };

        using (var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await Rewind.RunAsync(async () =>
            {
                order.Status = "paid";
                await Db.InsertAsync(committedKey, "paid");
            });
            transaction.Complete();
        }

        Assert.Equal("paid", order.Status);
        Assert.Equal(1, Db.Count(committedKey));

        using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await Rewind.RunAsync(async () =>
            {
                order.Status = "shipped";
                await Db.InsertAsync(rolledBackKey, "shipped");
            });
        }

        Assert.Equal("paid", order.Status);
        Assert.Equal(0, Db.Count(rolledBackKey));
    }
}

[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class PostgresTransactionTests(PostgresFixture db, ITestOutputHelper output) : TransactionScenarios(db, output)
{
    /// <summary>
    /// The database refuses the commit itself: a uniqueness deferred to COMMIT fails after the block and the scope
    /// have finished. The transaction aborts, memory rolls back with it, and no commit effect runs.
    /// </summary>
    [Fact]
    public void DatabaseRefusesTheCommit_MemoryRollsBack_CallerGetsTransactionAborted()
    {
        var entry = Db.NextKey();
        var order = new Order { Status = "new" };
        var committed = false;
        var rolledBack = false;

        var thrown = Record.Exception(() =>
        {
            using var transaction = new TransactionScope();
            Rewind.Run(scope =>
            {
                order.Status = "paid";
                order.Lines.Add("pen");
                using var connection = Db.Open();
                using var command = connection.CreateCommand();
                command.CommandText = $"INSERT INTO ledger (entry) VALUES ({entry}), ({entry})";
                command.ExecuteNonQuery();
                scope.AfterCommit(() => committed = true);
                scope.AfterRollback(() => rolledBack = true);
            });

            Assert.Equal("paid", order.Status);
            transaction.Complete();
        });

        Output.WriteLine($"{Db.Provider}: {thrown?.GetType().Name}: {thrown?.Message} / {thrown?.InnerException?.Message}");
        Assert.IsType<TransactionAbortedException>(thrown);
        Assert.Equal("new", order.Status);
        Assert.Empty(order.Lines);
        Assert.False(committed);
        Assert.True(rolledBack);
    }
}

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Database")]
public sealed class SqlServerTransactionTests(SqlServerFixture db, ITestOutputHelper output) : TransactionScenarios(db, output);
