# Transactions

A scope that runs inside a `System.Transactions` transaction — a `TransactionScope` around database work, for
instance — follows the outcome of that transaction. The database and the in-memory state then commit together or
roll back together, with nothing to configure.

Rewind does not make anything transactional on its own account: the database's work stays the database's, and
Rewind only follows the transaction it finds.

## Which transactions are joined

Only an ambient `System.Transactions` transaction: `Transaction.Current`, as a `TransactionScope` sets it. Entity
Framework Core and ADO.NET providers that support ambient transactions enlist the database connection in it, so the
database work and the scope commit and roll back together. Rewind itself enlists nothing: it follows the
transaction's outcome, so a transaction over one connection commits it in one phase exactly as without Rewind — no
distributed transaction, and no `PREPARE TRANSACTION` asked of PostgreSQL.

A transaction the database opens on its own is not seen: one opened with `DbContext.Database.BeginTransaction()` or
`DbConnection.BeginTransaction()`, and the one `SaveChanges` opens when there is no ambient transaction. Rewind has no
way to learn its outcome; this version has no integration for them. Wrap the work in a `TransactionScope` instead —
with `TransactionScopeAsyncFlowOption.Enabled` when the code awaits ([async.md](async.md)).

## How a scope follows the transaction

An outermost scope opened while `Transaction.Current` is an active transaction joins it:

- when the block completes, its changes stay in place, but the scope waits: its change notifications, its
  `OnCommitted` observers and its `AfterCommit` effects run only once the transaction commits;
- if the transaction rolls back — the `TransactionScope` is disposed without `Complete()`, or any participant votes
  against — the scope's changes are undone and its `AfterRollback` effects run. Its deferred effects never run.

```csharp
using System.Transactions;
using ViShap.Rewind;

var order = new Order { Status = "new" };

using (new TransactionScope())
{
    Rewind.Run(scope =>
    {
        order.Status = "paid";
        scope.AfterCommit(() => Console.WriteLine("Receipt sent"));
    });
}                                     // no Complete(): the transaction rolls back

Console.WriteLine(order.Status);      // new

using (var transaction = new TransactionScope())
{
    Rewind.Run(scope =>
    {
        order.Status = "paid";
        scope.AfterCommit(() => Console.WriteLine("Receipt sent")); // Receipt sent
    });
    transaction.Complete();
}

Console.WriteLine(order.Status);      // paid

[Rewindable]
public partial class Order
{
    public partial string Status { get; set; }
}
```

Several scopes in one transaction are undone together, the newest first. A nested scope belongs to its outermost
scope and behaves as always. A scope opened with no transaction — or with `TransactionScopeOption.Suppress`, or
before the `TransactionScope` began — commits at once, as without transactions.

## When the block fails

A scope that rolls back inside a transaction also rolls the transaction back. If the code catches the block's
exception and still calls `Complete()`, the database work done before the failure is not committed while the memory
was restored: disposing the transaction scope throws `TransactionAbortedException`.

```csharp
using System.Transactions;
using ViShap.Rewind;

var stock = new RewindCell<int>(5);

try
{
    using var transaction = new TransactionScope();
    try
    {
        Rewind.Run(() =>
        {
            stock.Value -= 1;
            throw new InvalidOperationException("Payment declined");
        });
    }
    catch (InvalidOperationException)
    {
    }

    transaction.Complete();
}
catch (TransactionAbortedException)
{
    Console.WriteLine("Rolled back together"); // Rolled back together
}

Console.WriteLine(stock.Value);               // 5
```

## Failures at the end of the transaction

The scope's deferred work runs once the transaction has completed and every participant of it has its outcome, so
its failures surface from disposing the `TransactionScope`: a failing `AfterCommit` effect or notification as
`RewindEffectException`, with the state committed; a failing compensation at a rollback as
`RewindCompensationException`, with the state restored.

A transaction can also complete on another thread — a distributed transaction, or one that times out. Then nothing is
thrown, since no caller would receive it; the failures are counted by the `rewind.effect.failures` and
`rewind.compensation.failures` metrics ([observability.md](observability.md)).

If the outcome of a distributed transaction is in doubt, the in-memory changes stay and their notifications and
`OnCommitted` observers run, but neither `AfterCommit` nor `AfterRollback` effects do: nothing is announced that may
not have happened.

## While the transaction is open

A scope that joined a transaction is closed but its outcome is not final, and Rewind does not isolate it: its changes
are visible to everyone, and anyone can write the same state. Two consequences:

- if the transaction rolls back, the state the scope changed returns to what it was before the scope — over anything
  written to it since, even by another scope that committed. Until the transaction ends, leave that state to the code
  inside the transaction;
- if the transaction times out, it is rolled back on a timer thread, which restores the state while your code may
  still be running and writing it. Give the `TransactionScope` a timeout longer than the work it covers, and stop
  using that state once a timeout is possible.
