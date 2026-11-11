# Collections

`RewindList<T>`, `RewindDictionary<TKey, TValue>` and `RewindSet<T>` are tracked state: a mutation made while a
scope is open is undone when that scope rolls back. They implement the same interfaces as `List<T>`,
`Dictionary<TKey, TValue>` and `HashSet<T>`, so most code that uses those works unchanged.

| Collection | Interfaces |
|---|---|
| `RewindList<T>` | `IList<T>`, `IReadOnlyList<T>` |
| `RewindDictionary<TKey, TValue>` | `IDictionary<TKey, TValue>`, `IReadOnlyDictionary<TKey, TValue>` |
| `RewindSet<T>` | `ISet<T>`, `IReadOnlySet<T>` |

## Rolling back mutations

Each mutation records how to undo itself: an `Add` remembers the position it took, a `Remove` the element and where
it was, `this[key] = value` the previous value or that the key was absent. A rollback undoes them newest first, so
the collection returns to exactly what it held when the scope opened.

```csharp
using ViShap.Rewind;

var cart = new RewindList<string>(["book"]);
var stock = new RewindDictionary<string, int> { ["book"] = 3, ["pen"] = 10 };
var coupons = new RewindSet<string>(["WELCOME"]);

try
{
    Rewind.Run(() =>
    {
        cart.Add("pen");
        stock["pen"] -= 1;
        coupons.Remove("WELCOME");
        throw new InvalidOperationException("Payment declined");
    });
}
catch (InvalidOperationException)
{
}

Console.WriteLine(string.Join(",", cart));   // book
Console.WriteLine(stock["pen"]);              // 10
Console.WriteLine(coupons.Contains("WELCOME")); // True
```

An operation that changes nothing — removing an absent element, `TryAdd` of a present key — records nothing. The
set operations `UnionWith`, `IntersectWith`, `ExceptWith` and `SymmetricExceptWith`, and `RewindList<T>.AddRange`,
record one entry holding exactly the elements they changed.

## `Clear`

`Clear` inside a scope does not copy the elements. The collection sets its current storage aside, starts an empty
one, and a rollback puts the old storage back. Both the `Clear` and its rollback take the same time whatever the
size of the collection.

```csharp
using ViShap.Rewind;

var orders = new RewindList<int>(Enumerable.Range(1, 1000));

using (Rewind.Begin())
{
    orders.Clear();
    orders.Add(42);
}

Console.WriteLine(orders.Count); // 1000
```

## Nested scopes

Collections follow the rules of every tracked value: a nested scope that rolls back undoes only its own mutations;
a nested scope that commits hands them to its parent, which can still undo them.

```csharp
using ViShap.Rewind;

var tags = new RewindSet<string>();

Rewind.Run(() =>
{
    tags.Add("paid");
    try
    {
        Rewind.Run(() =>
        {
            tags.UnionWith(["shipped", "delivered"]);
            throw new InvalidOperationException("Carrier unavailable");
        });
    }
    catch (InvalidOperationException)
    {
    }
});

Console.WriteLine(string.Join(",", tags)); // paid
```

## What is restored

- A rollback restores which elements a collection holds and, for a list, their positions. For a dictionary or a
  set, the order in which it enumerates its elements is not part of what is restored.
- Elements are not tracked. For a reference element, a change made inside the object is not undone: an immutable
  element type is the safe choice.
- A dictionary or set keeps the comparer it was created with, including across `Clear`.
- `Keys` and `Values` of a dictionary are read-only views that always show its current content.

## Enumeration and threads

Mutating a collection while enumerating it throws `InvalidOperationException`, as for the BCL collections — and so
does a rollback that changes it during the enumeration. `foreach` over a collection uses a struct enumerator.

A mutation with no scope open is a plain mutation, and nothing is recorded. A collection is not safe for concurrent
mutation from several threads, and a scope does not undo a mutation made on another thread.
