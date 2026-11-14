# Rewindable types

Marking a class `[Rewindable]` lets its properties be tracked state with ordinary property syntax. Declare each
property to track as `partial`; the source generator, which comes with the `ViShap.Rewind` package, writes its
implementation. A write made while a scope is open is undone when that scope rolls back — exactly as a write to a
`RewindCell<T>`. Partial properties need C# 13, the default from .NET 9; a project that targets .NET 8 sets
`<LangVersion>13</LangVersion>` or later ([getting-started.md](getting-started.md#install)).

```csharp
using ViShap.Rewind;

var order = new Order { Status = "new" };

try
{
    Rewind.Run(() =>
    {
        order.Total += 30m;
        order.Status = "paid";
        order.Lines.Add("book");
        throw new InvalidOperationException("Payment declined");
    });
}
catch (InvalidOperationException)
{
}

Console.WriteLine($"{order.Total} {order.Status} {order.Lines.Count}"); // 0 new 0

[Rewindable]
public partial class Order
{
    public partial decimal Total { get; set; }

    public partial string Status { get; set; }

    public RewindList<string> Lines { get; } = new();
}
```

## The rules

- The class must be `partial`, and so must every class it is nested in.
- Each `partial` property needs a `get` and a `set` accessor. Accessor modifiers (`private set`), `virtual`,
  `required` and nullable annotations are kept as declared.
- A property that is not `partial` is not tracked. A collection property is tracked through the collection itself:
  make it a `RewindList<T>`, `RewindDictionary<TKey, TValue>` or `RewindSet<T>`.
- `[RewindIgnore]` on a `partial` property makes it plain storage that a rollback leaves as written. On another
  writable property it states that the property is deliberately untracked.
- Reading a property reads a field. With no scope open, writing it is a plain assignment and allocates nothing.

```csharp
using ViShap.Rewind;

var account = new Account { Owner = "Ana" };
account.Deposit(100m);

using (Rewind.Begin())
{
    account.Deposit(50m);
    account.Views++;
}

Console.WriteLine($"{account.Balance} {account.Views}"); // 100 1

[Rewindable]
public partial class Account
{
    public partial decimal Balance { get; private set; }

    public required partial string Owner { get; set; }

    [RewindIgnore]
    public partial int Views { get; set; }

    public void Deposit(decimal amount) => Balance += amount;
}
```

## What is restored

As with a `RewindCell<T>`, a property of a reference type restores which object it refers to, not changes made
inside that object. Prefer immutable types — a `record` with `init` properties, `string`, the types of
`System.Collections.Immutable` — or another rewindable type, whose own properties are tracked.

```csharp
using ViShap.Rewind;

var customer = new Customer { Address = new Address("Yerevan") };

using (Rewind.Begin())
{
    customer.Address = customer.Address with { City = "Gyumri" };
}

Console.WriteLine(customer.Address.City); // Yerevan

public sealed record Address(string City);

[Rewindable]
public partial class Customer
{
    public partial Address Address { get; set; }
}
```

## Diagnostics

The compiler reports what cannot be tracked:

| Id | Severity | Meaning |
|---|---|---|
| [RWD0001](diagnostics.md#rwd0001) | Error | a `[Rewindable]` class, or a type it is nested in, is not `partial` |
| [RWD0002](diagnostics.md#rwd0002) | Warning | a writable auto-property is not `partial`: a rollback does not restore it |
| [RWD0003](diagnostics.md#rwd0003) | Error | a `partial` property has an `init` accessor or returns by reference |
| [RWD0004](diagnostics.md#rwd0004) | Warning | a `partial` property has a mutable reference type: only the reference is tracked |
| [RWD0005](diagnostics.md#rwd0005) | Error | a `partial` property is `static`, an indexer, or has no `set` accessor |

Each is explained, with its fix, in [Diagnostics](diagnostics.md).
