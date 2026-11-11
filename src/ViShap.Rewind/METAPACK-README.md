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

var account = new Account { Balance = 100m };

try
{
    Rewind.Run(() =>
    {
        account.Balance -= 30m;
        throw new InvalidOperationException("Payment declined");
    });
}
catch (InvalidOperationException)
{
}

Console.WriteLine(account.Balance); // 100

[Rewindable]
public partial class Account
{
    public partial decimal Balance { get; set; }
}
```
