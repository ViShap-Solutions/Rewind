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
| `ViShap.Rewind.Core` | Scopes, tracked state, effects, files, exceptions |
| `ViShap.Rewind.Generator` | Source generator and analyzers; compiler only |
| `ViShap.Rewind.Testing` | For test projects: proves an operation leaves no trace at every failure point |

A class marked `[Rewindable]` declares its tracked properties as `partial`; the source generator writes them. For a
single value outside a class there is `RewindCell<T>`, and `RewindList<T>`, `RewindDictionary<TKey, TValue>` and
`RewindSet<T>` are tracked collections.

Targets .NET 10.

## What it does not do

- **No isolation between threads.** A scope restores what its own thread changed; other threads see changes as they
  are made. State shared between requests — a singleton, a static cache — is not protected by Rewind.
- **No durability.** State lives in memory; a database is the durable store.
- **Only an ambient `System.Transactions` transaction is joined.** A `TransactionScope` around database work commits
  and rolls back together with the scope; a transaction opened with `DbContext.Database.BeginTransaction()`, or the
  one `SaveChanges` opens on its own, is not seen.

## Documentation

Start with [getting started](docs/getting-started.md). One page per subject: [scopes](docs/scopes.md),
[effects](docs/effects.md), [exceptions](docs/exceptions.md), [rewindable types](docs/rewindable-types.md),
[collections](docs/collections.md), [changes and history](docs/changes-and-history.md),
[transactions](docs/transactions.md), [files](docs/files.md), [asynchronous scopes](docs/async.md),
[observability](docs/observability.md), [testing](docs/testing.md), [diagnostics](docs/diagnostics.md).

## License

MIT
