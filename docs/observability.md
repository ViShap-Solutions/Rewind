# Observability

Rewind reports what its scopes do through the standard .NET diagnostics: a `Meter` and an `ActivitySource`, both
named `ViShap.Rewind` (`RewindDiagnostics.MeterName`, `RewindDiagnostics.ActivitySourceName`). Nothing is measured or
traced until a listener subscribes to these names.

With OpenTelemetry:

```text
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(RewindDiagnostics.MeterName))
    .WithTracing(tracing => tracing.AddSource(RewindDiagnostics.ActivitySourceName));
```

## Metrics

| Instrument | Type | Unit | Tags | Measures |
|---|---|---|---|---|
| `rewind.scope.opened` | Counter | `{scope}` | `rewind.scope.kind`, `rewind.scope.nested` | Scopes opened |
| `rewind.scope.closed` | Counter | `{scope}` | `rewind.scope.kind`, `rewind.scope.nested`, `rewind.scope.outcome` | Scopes whose outcome is final |
| `rewind.scope.entries` | Histogram | `{entry}` | `rewind.scope.kind`, `rewind.scope.outcome` | Journal entries of an outermost scope, at its outcome |
| `rewind.compensation.failures` | Counter | `{failure}` | — | Compensations and rollback effects that threw |
| `rewind.effect.failures` | Counter | `{failure}` | — | Notifications, `OnCommitted` observers and `AfterCommit` effects that threw |

- `rewind.scope.kind` is `sync` or `async` (a scope of `Rewind.RunAsync`); `rewind.scope.nested` is `true` for a
  scope opened inside another.
- `rewind.scope.outcome` is `committed`, `rolled_back` or `in_doubt`. A nested scope is closed when it commits into
  its parent or rolls back; an outermost scope that joined a transaction is closed when the transaction ends, with
  its outcome.

```csharp
using System.Diagnostics.Metrics;
using ViShap.Rewind;

var closed = new Dictionary<string, long>();
using var listener = new MeterListener();
listener.InstrumentPublished = (instrument, subscriber) =>
{
    if (instrument.Meter.Name == RewindDiagnostics.MeterName && instrument.Name == "rewind.scope.closed")
    {
        subscriber.EnableMeasurementEvents(instrument);
    }
};
listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
{
    foreach (var tag in tags)
    {
        if (tag.Key == "rewind.scope.outcome")
        {
            var outcome = (string)tag.Value!;
            closed[outcome] = closed.GetValueOrDefault(outcome) + value;
        }
    }
});
listener.Start();

var stock = new RewindCell<int>(5);
Rewind.Run(() => stock.Value -= 1);
try
{
    Rewind.Run(() =>
    {
        stock.Value -= 1;
        throw new InvalidOperationException("Payment declined");
    });
}
catch (InvalidOperationException)
{
}

Console.WriteLine($"{closed["committed"]} {closed["rolled_back"]}");   // 1 1
```

## Traces

Each outermost scope is an activity named `rewind.scope`. It is the current activity while the scope is open, so the
activities the block starts — a database call, an HTTP request — are its children, and it ends after the scope's
effects, which therefore run inside it. Its tags:

- `rewind.scope.kind` — `sync` or `async`;
- `rewind.scope.outcome` — `committed`, `rolled_back`, or `pending` for a scope that joined a transaction, whose
  activity ends when the scope closes; the transaction's outcome reaches the metrics;
- `rewind.scope.entries` — the journal entries recorded;
- `rewind.scope.failures` — the compensations or effects that threw.

A scope rolled back because its block threw has the status `Error` and an `exception` event. A nested scope has no
activity of its own; when it rolls back, the outermost activity gets an event `rewind.scope.rolled_back` with the
nested scope's `rewind.scope.depth`.

## Cost

With no listener, each scope checks whether an instrument is enabled and whether the source has listeners, and
allocates nothing for diagnostics. Writes to tracked state do nothing more than before.
