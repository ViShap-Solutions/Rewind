using System.Transactions;
using Microsoft.EntityFrameworkCore;
using ViShap.Rewind.Integration.Tests.Fixtures;

namespace ViShap.Rewind.Integration.Tests.EfCore;

/// <summary>
/// EF Core over PostgreSQL. Rewind joins an ambient <see cref="TransactionScope"/>, so EF Core inside one commits
/// and rolls back with tracked memory. A transaction opened with <c>Database.BeginTransaction()</c> is not a
/// System.Transactions transaction: Rewind does not see it, and its scope commits on its own. Both are documented;
/// these tests hold the library to the documentation.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class EfCoreTests(PostgresFixture db, ITestOutputHelper output)
{
    [Fact]
    public void TransactionScope_Completed_SaveChangesAndMemoryCommitTogether()
    {
        var key = db.NextKey();
        var order = new Order { Status = "new" };

        using (var transaction = new TransactionScope())
        using (var context = Context())
        {
            Rewind.Run(() =>
            {
                order.Status = "paid";
                context.Orders.Add(new OrderRow { Id = key, Status = "paid" });
                context.SaveChanges();
            });
            transaction.Complete();
        }

        Assert.Equal("paid", order.Status);
        Assert.Equal(1, Rows(key));
    }

    [Fact]
    public void TransactionScope_NotCompleted_SaveChangesAndMemoryRollBack()
    {
        var key = db.NextKey();
        var order = new Order { Status = "new" };

        using (new TransactionScope())
        using (var context = Context())
        {
            Rewind.Run(() =>
            {
                order.Status = "paid";
                context.Orders.Add(new OrderRow { Id = key, Status = "paid" });
                context.SaveChanges();
            });
        }

        Assert.Equal("new", order.Status);
        Assert.Equal(0, Rows(key));
    }

    [Fact]
    public void TransactionScope_SaveChangesFails_MemoryRollsBack()
    {
        var existing = db.NextKey();
        db.Insert(existing, "already there");
        var order = new Order { Status = "new" };

        var thrown = Record.Exception(() =>
        {
            using var transaction = new TransactionScope();
            using var context = Context();
            Rewind.Run(() =>
            {
                order.Status = "paid";
                context.Orders.Add(new OrderRow { Id = existing, Status = "duplicate" });
                context.SaveChanges();
            });
            transaction.Complete();
        });

        output.WriteLine($"{thrown?.GetType().Name}: {thrown?.InnerException?.Message}");
        Assert.IsType<DbUpdateException>(thrown);
        Assert.Equal("new", order.Status);
    }

    [Fact]
    public void DatabaseBeginTransaction_IsNotJoined_ScopeCommitsAtOnceAndKeepsItsChanges()
    {
        var key = db.NextKey();
        var order = new Order { Status = "new" };

        using (var context = Context())
        using (var transaction = context.Database.BeginTransaction())
        {
            Rewind.Run(() =>
            {
                order.Status = "paid";
                context.Orders.Add(new OrderRow { Id = key, Status = "paid" });
                context.SaveChanges();
            });

            transaction.Rollback();
        }

        output.WriteLine($"After the EF Core transaction rolled back: memory '{order.Status}', rows {Rows(key)} — use a TransactionScope to tie them together.");
        Assert.Equal("paid", order.Status);
        Assert.Equal(0, Rows(key));
    }

    private int Rows(int key) => db.Count(key);

    private OrdersContext Context()
    {
        var context = new OrdersContext(new DbContextOptionsBuilder<OrdersContext>().UseNpgsql(db.ConnectionString).Options);
        return context;
    }
}

/// <summary>A row of the <c>orders</c> table the database fixture creates.</summary>
public sealed class OrderRow
{
    public int Id { get; set; }

    public string Status { get; set; } = string.Empty;
}

public sealed class OrdersContext(DbContextOptions<OrdersContext> options) : DbContext(options)
{
    public DbSet<OrderRow> Orders => Set<OrderRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<OrderRow>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(row => row.Status).HasColumnName("status");
        });
}
