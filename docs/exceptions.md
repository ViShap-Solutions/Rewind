# Exceptions

A rollback never hides the exception that caused it: it reaches the caller either itself or inside a
`RewindCompensationException`. Every exception the library raises on its own account derives from
`RewindException`.

| Exception | When | State after |
|---|---|---|
| The block's own exception | The block passed to `Run` threw, and every compensation and rollback reaction succeeded | Rolled back |
| `RewindCompensationException` | A rollback ran and one or more compensations (`undo`) or rollback reactions (`AfterRollback`) threw | Tracked state rolled back; the external results of the failed compensations are unknown |
| `RewindEffectException` | The outermost scope committed and one or more `AfterCommit` effects threw | Committed |
| `RewindScopeException` | A scope rule was broken: another thread, a closed scope, out of nesting order, an awaitable block, a scope left open by `Run`'s block; for `RunAsync`, a synchronous scope open around it, or a parallel branch or a late write into its scope | Nothing changed, or rolled back when `Run`'s block left a scope open; for `RunAsync`, its scope rolls back |
| `ArgumentNullException` | A `null` block, action or operation | Nothing changed |

## The block's own exception

When the block throws and the rollback succeeds, `Run` rethrows the same instance with its stack trace.

```csharp
using ViShap.Rewind;

var original = new InvalidOperationException("Payment declined");

try
{
    Rewind.Run(() => throw original);
}
catch (InvalidOperationException caught)
{
    Console.WriteLine(ReferenceEquals(caught, original)); // True
}
```

## `RewindCompensationException`

A rollback attempts every compensation and every rollback reaction, even when some throw. If any threw, the call
that closed the scope throws `RewindCompensationException`:

- `OriginalException` is the exception that caused the rollback — `null` when a scope from `Rewind.Begin` was
  disposed without a commit;
- `Failures` lists the exceptions the compensations and reactions threw, in the order they were thrown.

Tracked state is restored in every case.

```csharp
using ViShap.Rewind;

var seats = new RewindCell<int>(10);

try
{
    Rewind.Run(scope =>
    {
        seats.Value -= 2;
        scope.Effect(() => { }, () => throw new TimeoutException("Release timed out"));
        throw new InvalidOperationException("Card expired");
    });
}
catch (RewindCompensationException exception)
{
    Console.WriteLine(exception.OriginalException?.Message); // Card expired
    Console.WriteLine(exception.Failures[0].Message);        // Release timed out
}

Console.WriteLine(seats.Value); // 10
```

## `RewindEffectException`

The outermost scope committed, then one or more `AfterCommit` effects threw. The state stays committed and every
other effect still ran. `Failures` lists every failure; `InnerException` is the first.

```csharp
using ViShap.Rewind;

var stock = new RewindCell<int>(5);

try
{
    Rewind.Run(scope =>
    {
        stock.Value -= 1;
        scope.AfterCommit(() => throw new TimeoutException("Mail server unavailable"));
        scope.AfterCommit(() => Console.WriteLine("Invoice stored")); // Invoice stored
    });
}
catch (RewindEffectException exception)
{
    Console.WriteLine(exception.Failures.Count); // 1
}

Console.WriteLine(stock.Value); // 4
```

## `RewindScopeException`

A scope was used in a way its rules forbid. The call changes nothing, except where `Run`'s block left a scope it
began open: then `Run` rolls back everything the block did before throwing.

```csharp
using ViShap.Rewind;

var outer = Rewind.Begin();
var inner = Rewind.Begin();

try
{
    outer.Commit();
}
catch (RewindScopeException)
{
    Console.WriteLine(ReferenceEquals(Rewind.Current, inner)); // True
}

inner.Dispose();
outer.Dispose();
```
