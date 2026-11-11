# ViShap.Rewind.Generator

The source generator and analyzers of **ViShap.Rewind**. They run in the compiler only; no shipped assembly
references this package. It comes with the meta-package `ViShap.Rewind`, which is the package to install.

The generator writes the tracked implementation of the `partial` properties of a class marked `[Rewindable]`, so a
domain type keeps ordinary property syntax and still has its changes undone on rollback:

```csharp
using ViShap.Rewind;

var order = new Order();

using (Rewind.Begin())
{
    order.Total = 30m;
}

Console.WriteLine(order.Total); // 0

[Rewindable]
public partial class Order
{
    public partial decimal Total { get; set; }
}
```

## Diagnostics

| Id | Severity | Meaning |
|---|---|---|
| RWD0001 | Error | a `[Rewindable]` class, or a type it is nested in, is not `partial` |
| RWD0002 | Warning | a writable auto-property is not `partial`: a rollback does not restore it |
| RWD0003 | Error | a `partial` property has an `init` accessor or returns by reference |
| RWD0004 | Warning | a `partial` property has a mutable reference type: only the reference is tracked |
| RWD0005 | Error | a `partial` property is `static`, an indexer, or has no `set` accessor |
| RWD1001 | Warning | the scope returned by `Rewind.Begin()` is never disposed |
| RWD1002 | Warning | a call that changes something outside the process is made directly inside a scope |
| RWD1003 | Error | an asynchronous block is passed to `Rewind.Run` |
| RWD1004 | Warning | a `RewindCell<T>` holds a mutable reference type: only the reference is tracked |
| RWD1005 | Warning | a field of a `[Rewindable]` type is written inside a scope, and a rollback does not restore it |

Each is explained, with its fix, in [the diagnostics documentation](https://github.com/ViShap-Solutions/Rewind/blob/main/docs/diagnostics.md).
