# ViShap.Rewind

Atomic in-memory state change for .NET. A block of code either keeps all of its changes to tracked state or none
of them, and the effects it has outside the process are bound to that outcome.

This meta-package ships no code of its own. It brings:

| Package | What it holds |
|---|---|
| `ViShap.Rewind.Core` | Scopes, tracked cells, effects, exceptions |
| `ViShap.Rewind.Generator` | The source generator and analyzers; they run in the compiler only |

For tests, add `ViShap.Rewind.Testing` to the test project.

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
