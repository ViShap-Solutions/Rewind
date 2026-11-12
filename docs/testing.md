# Testing

The package `ViShap.Rewind.Testing`, for test projects only, proves that an operation is atomic. An operation has as
many ways to fail as it has **failure points**: tracked writes recorded by a scope, changes of Rewind collections,
compensations of `Effect` and `RewindFile`, and registrations of `AfterCommit`, `AfterRollback` and `OnCommitted`.
`RewindVerify` makes it fail at each of them in turn and checks that it leaves no trace.

A failed check is a `RewindVerificationException`, so it works with any test framework.

## `RewindVerify.Rollback`

```csharp
using ViShap.Rewind;
using ViShap.Rewind.Testing;

var order = new Order { Status = "new" };
var sent = new List<string>();

void Checkout()
{
    Rewind.Run(scope =>
    {
        order.Status = "paid";
        order.Total = 30m;
        scope.AfterCommit(() => sent.Add("receipt"));
    });
}

RewindVerify.Rollback(Checkout, () => (order.Status, order.Total, sent.Count));
Console.WriteLine("Checkout is atomic");          // Checkout is atomic

[Rewindable]
public partial class Order
{
    public partial string Status { get; set; }

    public partial decimal Total { get; set; }
}
```

`Rollback` first runs the operation once, inside a scope it rolls back, to count its failure points — and to check
that the rollback restores everything `capture` returns: a plain field written by the operation shows here. Then it
runs the operation once per failure point, as the application would, making it fail at that point, and after each
run checks two things:

- `capture` returns what it returned before the first run. Capture everything the operation may change: tracked
  state, untracked state, and the fakes of external systems — an outbox, a file;
- no deferred effect — `AfterCommit`, `OnCommitted` — ran.

The operation runs as written: the scopes it opens are its own and commit or roll back for real. That is what finds an
operation that is not atomic although each of its scopes is — one scope commits, the next one fails:

```csharp
using ViShap.Rewind;
using ViShap.Rewind.Testing;

var stock = new RewindCell<int>(5);
var balance = new RewindCell<decimal>(100m);

void Purchase()
{
    Rewind.Run(() => stock.Value -= 1);            // commits on its own
    Rewind.Run(() => balance.Value -= 30m);        // if this fails, the stock stays taken
}

try
{
    RewindVerify.Rollback(Purchase, () => (stock.Value, balance.Value));
}
catch (RewindVerificationException exception)
{
    Console.WriteLine($"{exception.FailurePoint} of {exception.FailurePointCount}: {exception.Point}");
    // 2 of 2: write to RewindCell<Decimal>
}
```

The fix is one scope around the whole operation. An operation that catches the injected failure and completes passes
when the state is as it was and no deferred effect ran.

`Rollback` also reports an operation that writes tracked state with no scope of its own, one that does not pass the
same failure points every time it runs, and one that fails without any injected failure (its exception is the inner
exception). Pass an `IEqualityComparer<TState>` when the captured state needs its own comparison. It must be called
with no scope open.

## `RewindVerify.Retries`

Code that is retried after a failure must behave the same on every attempt. `Retries` runs the operation several
times, each attempt inside a scope it rolls back, and checks that every attempt returns the same result, ends with the
same captured state and passes the same failure points as the first, and that each rollback restores the starting
state:

```csharp
using ViShap.Rewind;
using ViShap.Rewind.Testing;

var stock = new RewindCell<int>(5);
var attempts = 0;

try
{
    RewindVerify.Retries(() => Rewind.Run(() =>
    {
        stock.Value -= 1;
        return ++attempts;                          // a plain counter: each attempt returns another value
    }), times: 3, capture: () => stock.Value);
}
catch (RewindVerificationException exception)
{
    Console.WriteLine(exception.Message.Split('.')[0]);   // Attempt 2 disagrees with the first: it returned 2, the first attempt returned 1
}
```

## Asynchronous operations

`RollbackAsync` and `RetriesAsync` do the same for an operation returning a task. Every scope the operation opens in
its flow is observed, also after an `await` resumes on another thread.

## What is observed

Every outermost scope opened in the flow that runs the operation — on the calling thread, and wherever its `await`s
and the tasks it starts take it — gets a journal that reports each failure point. A scope opened in a flow the
operation did not start, such as a thread created with the execution context suppressed, is not observed. Outside a
verification nothing changes: the hook costs one read of a static field when an outermost scope opens.
