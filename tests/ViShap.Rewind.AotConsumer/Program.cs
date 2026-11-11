// A consumer of Core and the generator, published with native AOT by the CI job. It runs every public entry point
// of the engine and prints one line per outcome; the CI job compares the native output with the JIT output.

using ViShap.Rewind;

var cell = new RewindCell<int>(1);
var log = new List<string>();

Rewind.Run(() => { cell.Value = 2; });
Report("Run commits", cell.Value);

Attempt(() => Rewind.Run(() =>
{
    cell.Value = 3;
    throw new InvalidOperationException("declined");
}));
Report("Run rolls back", cell.Value);

Rewind.Run(cell, static c => c.Value = 4);
Report("Run with state", cell.Value);

Attempt(() => Rewind.Run(new FailingWrite(cell, 5)));
Report("Struct operation rolls back", cell.Value);

Report("Run returns", Rewind.Run(() => cell.Value * 10));

Attempt(() => Rewind.Run(() => Task.CompletedTask));
Attempt(() => Rewind.Run(() => ValueTask.FromResult(1)));

using (Rewind.Begin())
{
    cell.Value = 6;
}

Report("Dispose rolls back", cell.Value);

using (var scope = Rewind.Begin())
{
    cell.Value = 7;
    scope.Commit();
}

Report("Commit keeps", cell.Value);

Rewind.Run(() =>
{
    cell.Value = 8;
    Attempt(() => Rewind.Run(() =>
    {
        cell.Value = 9;
        throw new InvalidOperationException("inner");
    }));
    Report("Inner rollback restores outer value", cell.Value);
});

Rewind.Run(scope =>
{
    scope.AfterCommit(() => log.Add("after commit"));
    scope.Effect(() => log.Add("do"), () => log.Add("undo"));
});

Attempt(() => Rewind.Run(scope =>
{
    scope.AfterCommit(() => log.Add("discarded"));
    scope.AfterRollback(() => log.Add("after rollback"));
    scope.Effect(() => log.Add("do"), () => log.Add("undo"));
    throw new InvalidOperationException("declined");
}));

Attempt(() => Rewind.Run(scope =>
{
    scope.Effect(() => { }, () => throw new InvalidOperationException("undo failed"));
    throw new InvalidOperationException("declined");
}));

Attempt(() => Rewind.Run(scope => scope.AfterCommit(() => throw new InvalidOperationException("effect failed"))));

Report("Effects", string.Join(", ", log));
Report("Current after all", Rewind.Current is null ? "none" : "open");

static void Report(string name, object value) => Console.WriteLine($"{name}: {value}");

static void Attempt(Action action)
{
    try
    {
        action();
        Console.WriteLine("Completed");
    }
    catch (Exception exception)
    {
        var failures = exception switch
        {
            RewindCompensationException compensation => $" ({compensation.Failures.Count} failure(s), original: {compensation.OriginalException?.Message})",
            RewindEffectException effect => $" ({effect.Failures.Count} failure(s))",
            _ => string.Empty,
        };
        Console.WriteLine($"Threw {exception.GetType().Name}{failures}");
    }
}

internal readonly struct FailingWrite(RewindCell<int> cell, int value) : IRewindOperation
{
    public void Execute()
    {
        cell.Value = value;
        throw new InvalidOperationException("declined");
    }
}
