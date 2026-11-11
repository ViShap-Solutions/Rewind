# Transactions

A scope that runs inside a `System.Transactions` transaction — a `TransactionScope` around database work, for
instance — follows the outcome of that transaction. The database and the in-memory state then commit together or
roll back together, with nothing to configure.

Rewind does not make anything transactional on its own account: the database's work stays the database's, and
Rewind only follows the transaction it finds.

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

The scope's deferred work runs while the transaction completes, so its failures surface from disposing the
`TransactionScope`: a failing `AfterCommit` effect or notification as `RewindEffectException`, with the state
committed; a failing compensation at a rollback as `RewindCompensationException`, with the state restored.

If the outcome of a distributed transaction is in doubt, the in-memory changes stay and their notifications and
`OnCommitted` observers run, but neither `AfterCommit` nor `AfterRollback` effects do: nothing is announced that may
not have happened.
