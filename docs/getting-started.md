# Getting started

Rewind makes a block of code an atomic unit of in-memory state change: when the block completes, its changes to
tracked state stay; when it throws, they are undone, the exception reaches the caller, and the work it did outside
the process is run, discarded or compensated according to how it was registered.

## Install

```text
dotnet add package ViShap.Rewind
```

The package brings the library and its source generator. Targets .NET 10. For tests, add `ViShap.Rewind.Testing` to
the test project ([testing.md](testing.md)).

## The first scope

```csharp
using ViShap.Rewind;

var account = new Account { Balance = 100m };
var history = new RewindList<string>();

try
{
    Rewind.Run(() =>
    {
        account.Balance -= 30m;
        history.Add("withdrawal");
        throw new InvalidOperationException("Payment declined");
    });
}
catch (InvalidOperationException)
{
}

Console.WriteLine($"{account.Balance} {history.Count}");   // 100 0

Rewind.Run(() =>
{
    account.Balance -= 30m;
    history.Add("withdrawal");
});

Console.WriteLine($"{account.Balance} {history.Count}");   // 70 1

[Rewindable]
public partial class Account
{
    public partial decimal Balance { get; set; }
}
```

`Rewind.Run` opens a **scope**, runs the block, and commits when it returns or rolls back when it throws. Scopes nest;
`Rewind.Begin()` opens one you commit yourself; `Rewind.RunAsync` runs an asynchronous block ([scopes.md](scopes.md),
[async.md](async.md)).

## What is tracked

A rollback restores **tracked state** and nothing else:

- the `partial` properties of a class marked `[Rewindable]`, written by the source generator
  ([rewindable-types.md](rewindable-types.md));
- a `RewindCell<T>`, for a single value outside such a class;
- `RewindList<T>`, `RewindDictionary<TKey, TValue>` and `RewindSet<T>` ([collections.md](collections.md)).

A plain field, a plain property, a `List<T>` keep whatever value the block left in them. The analyzers report the
usual mistakes — a writable property that is not `partial`, a field written inside a scope — at compile time
([diagnostics.md](diagnostics.md)).

## Work outside the process

Mail, messages, files and calls to other systems cannot be undone like memory. Register them with the scope instead of
calling them directly: `AfterCommit` runs only once the change is kept, `Effect` runs now and is compensated if the
scope rolls back ([effects.md](effects.md)). Files have `RewindFile` ([files.md](files.md)).

```csharp
using ViShap.Rewind;

var stock = new RewindCell<int>(5);

Rewind.Run(scope =>
{
    stock.Value -= 1;
    scope.AfterCommit(() => Console.WriteLine("Order confirmed"));   // Order confirmed
});
```

## What Rewind does not do

- **It does not isolate threads from each other.** A scope restores the state its own thread changed. Another thread
  sees the changes as they are made, before the commit, and its own writes are not undone by this scope. State
  shared between requests — a singleton in a web application, a static cache — is not protected by Rewind: guard it
  with a lock or keep it per request.
- **It does not make anything durable.** The state lives in memory; a process that ends loses it. A database is the
  durable store.
- **It joins only an ambient `System.Transactions` transaction.** Inside a `TransactionScope`, a scope commits or rolls
  back with the transaction. A transaction opened another way — `DbContext.Database.BeginTransaction()`, or the one
  `SaveChanges` opens on its own — is not seen ([transactions.md](transactions.md)).

## Next

[scopes.md](scopes.md) explains nesting, commit and rollback in full; [exceptions.md](exceptions.md) what each
exception leaves behind; [changes-and-history.md](changes-and-history.md) change sets, `PropertyChanged` after the
commit, and undo and redo; [observability.md](observability.md) metrics and traces.
