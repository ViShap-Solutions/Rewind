# Scopes

A **scope** is the boundary of an atomic unit of change. While a scope is open on a thread, every write to tracked
state on that thread is remembered; when the scope commits, the writes stay; when it rolls back, every value is put
back to what it was when the scope opened.

Tracked state is `RewindCell<T>` and the members built on it. A plain field or property is not tracked: a rollback
leaves it as the block left it.

## `Rewind.Run`

`Rewind.Run` runs a block in a new scope. When the block returns, the scope commits. When the block throws, the
scope rolls back and the same exception instance reaches the caller.

```csharp
using ViShap.Rewind;

var balance = new RewindCell<decimal>(100m);

Rewind.Run(() => balance.Value -= 30m);
Console.WriteLine(balance.Value); // 70

try
{
    Rewind.Run(() =>
    {
        balance.Value -= 50m;
        throw new InvalidOperationException("Payment declined");
    });
}
catch (InvalidOperationException exception)
{
    Console.WriteLine(exception.Message); // Payment declined
}

Console.WriteLine(balance.Value); // 70
```

The overloads differ only in what the block receives and returns:

| Overload | Use |
|---|---|
| `Run(Action)` | A block with no result |
| `Run(Action<RewindScope>)` | A block that registers effects on its scope (see [Effects](effects.md)) |
| `Run<TState>(TState, Action<TState>)` | A `static` lambda that captures nothing and takes its state as an argument |
| `Run<TOperation>(TOperation)` | A domain operation implementing `IRewindOperation`; a struct is run without a delegate and without boxing |
| `Run<TResult>(Func<TResult>)` | A block with a result, returned after the commit |
| `Run<TResult>(Func<RewindScope, TResult>)` | Both |

```csharp
using ViShap.Rewind;

var stock = new RewindCell<int>(5);

var left = Rewind.Run(() => stock.Value -= 1);
Console.WriteLine(left); // 4

Rewind.Run(stock, static s => s.Value += 10);
Console.WriteLine(stock.Value); // 14

Rewind.Run(new Reserve(stock, 3));
Console.WriteLine(stock.Value); // 11

readonly struct Reserve(RewindCell<int> stock, int quantity) : IRewindOperation
{
    public void Execute()
    {
        if (stock.Value < quantity)
        {
            throw new InvalidOperationException("Not enough stock");
        }

        stock.Value -= quantity;
    }
}
```

The scope a block receives belongs to `Run`: the block must not call `Commit` or `Dispose` on it. Doing so throws
`RewindScopeException`, and the scope rolls back.

## `Rewind.Begin`

`Rewind.Begin` opens a scope that the caller closes. `Commit` keeps the changes; disposing the scope without a
commit rolls them back. Use it where a lambda is awkward: early returns, `ref` locals, spans.

```csharp
using ViShap.Rewind;

var order = new RewindCell<string>("new");

Pay(order, approved: false);
Console.WriteLine(order.Value); // new

Pay(order, approved: true);
Console.WriteLine(order.Value); // paid

static void Pay(RewindCell<string> order, bool approved)
{
    using var scope = Rewind.Begin();
    order.Value = "paying";
    if (!approved)
    {
        return;
    }

    order.Value = "paid";
    scope.Commit();
}
```

`Dispose` of a committed scope does nothing, so `using` is always safe.

## What a rollback restores

A cell is restored to the value it held when the scope opened, however many times the block wrote it. For a
reference type, only the reference is restored: a change made inside the referenced object is not undone, so an
immutable type is the safe choice. The compiler warns about a cell of a mutable type
([RWD1004](diagnostics.md#rwd1004)); the example below suppresses the warning to show what it is about.

```csharp
using ViShap.Rewind;

#pragma warning disable RWD1004
var tags = new RewindCell<List<string>>(["a"]);
#pragma warning restore RWD1004
var name = new RewindCell<string>("first");

using (Rewind.Begin())
{
    name.Value = "second";
    name.Value = "third";
    tags.Value.Add("b");
    tags.Value = ["x"];
}

Console.WriteLine(name.Value);                 // first
Console.WriteLine(string.Join(",", tags.Value)); // a,b
```

## Nesting

A scope opened while another is open on the same thread is its **child**. A child that rolls back undoes only
what was done since it opened. A child that commits hands its changes to its parent, which still decides: if the
parent rolls back later, the child's changes are undone too.

```csharp
using ViShap.Rewind;

var balance = new RewindCell<decimal>(100m);
var bonus = new RewindCell<decimal>(0m);

Rewind.Run(() =>
{
    balance.Value -= 30m;
    try
    {
        Rewind.Run(() =>
        {
            bonus.Value = 5m;
            throw new InvalidOperationException("Bonus unavailable");
        });
    }
    catch (InvalidOperationException)
    {
    }
});

Console.WriteLine($"{balance.Value} {bonus.Value}"); // 70 0
```

Scopes close innermost first. Committing or disposing a scope while a child of it is still open throws
`RewindScopeException` and changes nothing. If a block passed to `Run` leaves a scope it began open, `Run` rolls
back everything the block did and throws `RewindScopeException`.

`Rewind.Current` is the innermost open scope of the current thread, or `null`. `RewindScope.Depth` is 0 for an
outermost scope and one more for each enclosing scope.

## Threads

A scope belongs to the thread that opened it. Calling `Commit`, `Dispose`, `AfterCommit`, `AfterRollback` or
`Effect` on an open scope from another thread throws `RewindScopeException` and changes nothing. A scope is
invisible to other threads: a write from a thread with no open scope is a plain write, and another thread's
rollback does not undo it. A `RewindCell<T>` is not safe for concurrent writes.

## Asynchronous code

`Rewind.Run` refuses a block that returns `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>`: such a block would keep
running after its scope closed. It throws `RewindScopeException` before running the block.

```csharp
using ViShap.Rewind;

try
{
    _ = Rewind.Run(() => Task.CompletedTask);
}
catch (RewindScopeException)
{
    Console.WriteLine("Refused"); // Refused
}
```

Run the synchronous part of the work in the scope.
