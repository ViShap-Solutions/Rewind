# ViShap.Rewind.Testing

Test tools for code that uses **ViShap.Rewind**. Add it to a test project only.

An operation that is meant to be atomic has as many failure points as it has writes. This package runs the
operation once to count them, then runs it again failing at each one in turn, and checks after every run that
tracked state is exactly as it was and that no deferred effect ran. It finds the write that went to a plain field
instead of tracked state and the effect that was called directly instead of through the scope.

It works with any test framework: a failed check is an exception.

```csharp
using ViShap.Rewind;
using ViShap.Rewind.Testing;

var stock = new RewindCell<int>(5);
var balance = new RewindCell<decimal>(100m);

void Purchase()
{
    Rewind.Run(() =>
    {
        stock.Value -= 1;
        balance.Value -= 30m;
    });
}

RewindVerify.Rollback(Purchase, () => (stock.Value, balance.Value));   // throws RewindVerificationException if not atomic
Console.WriteLine("Purchase is atomic");                               // Purchase is atomic
```

`RewindVerify.Rollback` checks rollback at every failure point, `RewindVerify.Retries` that retried code behaves the
same on every attempt; `RollbackAsync` and `RetriesAsync` do the same for asynchronous operations.

More in the [documentation](https://github.com/ViShap-Solutions/Rewind/blob/main/docs/testing.md).
