# Effects

A scope can undo tracked state, but not an e-mail that was sent or a reservation made in another system. An
**effect** binds such work to the outcome of the scope. There are three kinds.

| Kind | API | When it runs |
|---|---|---|
| Deferred | `scope.AfterCommit(action)` | Once, after the outermost scope commits; never if any enclosing scope rolls back |
| Compensable | `scope.Effect(do, undo)` | `do` at once; `undo` if the scope or an enclosing one rolls back |
| Rollback reaction | `scope.AfterRollback(action)` | After a rollback has restored the state |

Effects are registered on the innermost open scope. Inside `Rewind.Run`, take the scope as the block's argument.

## Deferred: `AfterCommit`

Work that must not happen unless the change is kept: a message, a notification, a cache broadcast. A deferred
effect registered in a nested scope waits for the outermost commit, and is discarded if any scope enclosing its
registration rolls back.

```csharp
using ViShap.Rewind;

var stock = new RewindCell<int>(5);

Rewind.Run(scope =>
{
    stock.Value -= 1;
    scope.AfterCommit(() => Console.WriteLine("Order confirmed")); // Order confirmed
});

try
{
    Rewind.Run(scope =>
    {
        stock.Value -= 1;
        scope.AfterCommit(() => Console.WriteLine("Never printed"));
        throw new InvalidOperationException("Payment declined");
    });
}
catch (InvalidOperationException)
{
}

Console.WriteLine(stock.Value); // 4
```

Deferred effects run after the state is final, in registration order across every nested scope, with no current
scope. A failing effect does not undo the state and does not stop the others; once all have run, the closing call
throws `RewindEffectException` with every failure.

Work that cannot be undone at all — printing, dispensing cash — belongs here, registered last.

## Compensable: `Effect`

Work that happens at once and has a way to be cancelled. `do` runs immediately; `undo` is kept and runs on
rollback, newest first, interleaved with the restoration of tracked state — so each `undo` sees the state as it was
just after its `do`.

```csharp
using ViShap.Rewind;

var seats = new RewindCell<int>(10);

try
{
    Rewind.Run(scope =>
    {
        seats.Value -= 2;
        scope.Effect(@do: () => Console.WriteLine("Hold placed"),        // Hold placed
                     undo: () => Console.WriteLine("Hold released"));   // Hold released
        throw new InvalidOperationException("Card expired");
    });
}
catch (InvalidOperationException)
{
}

Console.WriteLine(seats.Value); // 10
```

If `do` throws, `undo` is not registered and the exception leaves `Effect`.

## Rollback reaction: `AfterRollback`

Work to do once a rollback has restored the state: releasing a lock in the user interface, recording that an
attempt failed.

```csharp
using ViShap.Rewind;

var balance = new RewindCell<decimal>(100m);

try
{
    Rewind.Run(scope =>
    {
        balance.Value -= 150m;
        scope.AfterRollback(() => Console.WriteLine($"Restored to {balance.Value}")); // Restored to 100
        throw new InvalidOperationException("Insufficient funds");
    });
}
catch (InvalidOperationException)
{
}
```

## Nested scopes

A nested scope that commits passes all three kinds to its parent. A nested scope that rolls back discards its
deferred effects, runs its compensations and runs its rollback reactions; its parent's effects are untouched.

```csharp
using ViShap.Rewind;

Rewind.Run(outer =>
{
    outer.AfterCommit(() => Console.WriteLine("Outer committed"));

    try
    {
        Rewind.Run(inner =>
        {
            inner.AfterCommit(() => Console.WriteLine("Never printed"));
            inner.AfterRollback(() => Console.WriteLine("Inner rolled back"));
            throw new InvalidOperationException();
        });
    }
    catch (InvalidOperationException)
    {
    }
});

// Inner rolled back   — at once, when the inner scope rolls back
// Outer committed     — once the outermost scope commits
```

## What runs with no current scope

`undo`, `AfterRollback` and `AfterCommit` run with no current scope: `Rewind.Current` is `null`, and a write to
tracked state there is a plain write that nothing records.

## Asynchronous work

The effects of this version are synchronous: `AfterCommit`, `AfterRollback` and `Effect` take an `Action`. An `async`
lambda passed to them would become `async void` — nothing would wait for it and its exception would be lost — so
analyzer RWD1003 reports it as an error ([diagnostics.md](diagnostics.md#rwd1003)). Do asynchronous work that must
follow the commit after the scope returns — after `Rewind.Run`, or after `await Rewind.RunAsync(…)` — or hand it from
a synchronous `AfterCommit` to a queue or an outbox that runs it.
