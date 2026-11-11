<p align="center">
  <img src="icon-transparent.png" alt="Rewind Logo" width="150px" />
</p>

# Rewind

> **Atomic in-memory state for .NET — every change kept, or none.**

[![NuGet](https://img.shields.io/nuget/v/ViShap.Rewind.svg)](https://www.nuget.org/packages/ViShap.Rewind)
[![GitHub Actions](https://github.com/ViShap-Solutions/Rewind/actions/workflows/ci.yml/badge.svg)](https://github.com/ViShap-Solutions/Rewind/actions/workflows/ci.yml)
[![Releases](https://img.shields.io/github/v/release/ViShap-Solutions/Rewind.svg)](https://github.com/ViShap-Solutions/Rewind/releases)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-2ea44f)](LICENSE)

Atomic in-memory state change for .NET. A block of code either keeps all of its changes to tracked state or none
of them — and what it does outside the process is bound to that outcome: sent only after the change is kept,
or compensated when it is not.

```csharp
using ViShap.Rewind;

var account = new Account { Balance = 100m };

try
{
    Rewind.Run(scope =>
    {
        account.Balance -= 30m;
        account.Reserved += 1;
        scope.AfterCommit(() => Console.WriteLine("Receipt sent"));
        throw new InvalidOperationException("Payment declined");
    });
}
catch (InvalidOperationException)
{
}

Console.WriteLine($"{account.Balance} {account.Reserved}"); // 100 0, and no receipt was sent

[Rewindable]
public partial class Account
{
    public partial decimal Balance { get; set; }

    public partial int Reserved { get; set; }
}
```

## Packages

| Package | What it holds |
|---|---|
| `ViShap.Rewind` | The meta-package to install: brings the two below |
| `ViShap.Rewind.Core` | Scopes, tracked state, effects, exceptions |
| `ViShap.Rewind.Generator` | Source generator and analyzers; compiler only |
| `ViShap.Rewind.Testing` | For test projects: proves an operation leaves no trace at every failure point |

A class marked `[Rewindable]` declares its tracked properties as `partial`; the source generator writes them. For a
single value outside a class there is `RewindCell<T>`, and `RewindList<T>`, `RewindDictionary<TKey, TValue>` and
`RewindSet<T>` are tracked collections.

Targets .NET 10.

## Documentation

`docs/` holds the consumer documentation, one page per subject.

## License

MIT
