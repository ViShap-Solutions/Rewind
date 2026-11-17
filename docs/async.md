# Asynchronous scopes

`Rewind.RunAsync` runs an asynchronous block in a scope that follows it through every `await`: when the block's task
completes, its changes stay; when it throws or is canceled, every change it made — before and after each `await`,
on whatever thread — is undone, and the exception reaches the caller.

```csharp
using ViShap.Rewind;

var stock = new RewindCell<int>(5);
var reserved = new RewindList<string>();

try
{
    await Rewind.RunAsync(async () =>
    {
        stock.Value -= 1;
        await Task.Yield();                       // the block may resume on another thread
        reserved.Add("order-1");
        throw new InvalidOperationException("Payment declined");
    });
}
catch (InvalidOperationException)
{
}

Console.WriteLine($"{stock.Value} {reserved.Count}");   // 5 0

var total = await Rewind.RunAsync(async scope =>
{
    stock.Value -= 1;
    await Task.Yield();
    scope.AfterCommit(() => Console.WriteLine("Reserved"));   // Reserved
    return stock.Value;
});

Console.WriteLine(total);                         // 4
```

There are four overloads: with or without the scope as the block's parameter, with or without a result. A `null`
block throws at the call; every other exception — the block's own, `RewindCompensationException`,
`RewindEffectException` — is carried by the returned task. A canceled block rolls back and the task is canceled.

The commit, the change notifications and the effects run where the block's last `await` resumed. With a
synchronization context — a UI thread — that is the caller's context, so `PropertyChanged` handlers and effects run
there.

## Nesting

Inside `RunAsync`, a synchronous `Rewind.Run` and an awaited `Rewind.RunAsync` are child scopes: rolling one back
undoes only what it did, and the outer scope still decides about what it committed.

```csharp
using ViShap.Rewind;

var balance = new RewindCell<decimal>(100m);

await Rewind.RunAsync(async () =>
{
    balance.Value -= 30m;
    try
    {
        await Rewind.RunAsync(async () =>
        {
            balance.Value -= 50m;
            await Task.Yield();
            throw new InvalidOperationException("Limit exceeded");
        });
    }
    catch (InvalidOperationException)
    {
    }

    Console.WriteLine(balance.Value);             // 70
});
```

The other way round is refused: `RunAsync` while a synchronous scope is open fails with `RewindScopeException`
before its block runs, because after an `await` the block could continue on another thread than the one the
synchronous scope belongs to.

## One flow at a time

An asynchronous scope belongs to the flow that opened it. Parallel branches started inside it — `Task.WhenAll`,
`Task.Run`, `Parallel.ForEachAsync` — would all see the same scope and write into its one journal, where a nested
scope of one branch could undo another branch's writes. So a write from a branch while another branch has a scope
open, a write at the same moment as another, and a write through a scope that has already closed are refused with
`RewindScopeException`; the scope then rolls back. Analyzer RWD1006 reports parallel branches started inside a
`RunAsync` block ([diagnostics.md](diagnostics.md#rwd1006)).

Two branches that interleave on one thread without scopes of their own cannot be told apart from one flow, so
nothing guarantees they are caught: keep branches out of a shared scope. To work in parallel, give each branch its
own outermost scope:

```csharp
using ViShap.Rewind;

var first = new RewindCell<int>(0);
var second = new RewindCell<int>(0);

await Task.WhenAll(
    Rewind.RunAsync(async () => { await Task.Yield(); first.Value = 1; }),
    Rewind.RunAsync(async () => { await Task.Yield(); second.Value = 2; }));

Console.WriteLine($"{first.Value} {second.Value}");   // 1 2
```

Each branch then commits or rolls back on its own. Rewind does not isolate threads from each other: the branches
must not write the same tracked state.

## Effects

`AfterCommit`, `AfterRollback` and `Effect` take synchronous actions in this version, also in an asynchronous scope.
An `async` lambda passed to one of them would become `async void` — nothing waits for it and its exception is lost —
and analyzer RWD1003 reports it as an error. Asynchronous work that must follow the commit is done after
`await Rewind.RunAsync(…)` returns, or handed from a synchronous effect to a queue that runs it
([effects.md](effects.md#asynchronous-work)).

## Transactions

An asynchronous scope joins an ambient transaction as a synchronous one does ([transactions.md](transactions.md)).
A `TransactionScope` flows through `await` only when it is created with
`TransactionScopeAsyncFlowOption.Enabled`:

```csharp
using System.Transactions;
using ViShap.Rewind;

var status = new RewindCell<string>("new");

using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
{
    await Rewind.RunAsync(async () =>
    {
        await Task.Yield();
        status.Value = "paid";
    });
}                                                 // no Complete(): the transaction rolls back

Console.WriteLine(status.Value);                  // new
```
