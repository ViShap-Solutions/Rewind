# ViShap.Rewind.Core

The engine of **ViShap.Rewind**. Most applications install the meta-package `ViShap.Rewind`, which brings this
package and the source generator together.

A block of code becomes an atomic unit of in-memory state change: when it completes, its changes to tracked state
stay; when it throws, they are undone and the exception reaches the caller.

```csharp
using ViShap.Rewind;

var balance = new RewindCell<decimal>(100m);

try
{
    Rewind.Run(() =>
    {
        balance.Value -= 30m;
        throw new InvalidOperationException("Payment declined");
    });
}
catch (InvalidOperationException)
{
}

Console.WriteLine(balance.Value); // 100
```

Effects that leave the process are bound to the outcome of the scope:

```csharp
using ViShap.Rewind;

var stock = new RewindCell<int>(5);

Rewind.Run(scope =>
{
    stock.Value -= 1;
    scope.AfterCommit(() => Console.WriteLine("Reserved"));          // runs only if the change is kept
    scope.Effect(@do: () => Console.WriteLine("Hold placed"),
                 undo: () => Console.WriteLine("Hold released"));    // undone if the change is not kept
});
```

Files written, copied or deleted with `RewindFile` change at once and are put back if the block fails.

What the library tracks is state it can restore: `RewindCell<T>`, the collections `RewindList<T>`,
`RewindDictionary<TKey, TValue>` and `RewindSet<T>`, and the members built on them. A plain field keeps whatever
value the block left in it.
