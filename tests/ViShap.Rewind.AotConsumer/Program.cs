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

var list = new RewindList<int>([1, 2, 3]);
var dictionary = new RewindDictionary<string, int> { ["a"] = 1 };
var set = new RewindSet<string>(["x"], StringComparer.Ordinal);

Attempt(() => Rewind.Run(() =>
{
    list.Add(4);
    list.RemoveAt(0);
    list.Clear();
    dictionary["a"] = 2;
    dictionary.Add("b", 3);
    set.UnionWith(["y", "z"]);
    set.Clear();
    throw new InvalidOperationException("declined");
}));
Report("Collections roll back", $"{string.Join(",", list)} {dictionary["a"]}/{dictionary.Count} {string.Join(",", set.Order(StringComparer.Ordinal))}");

Rewind.Run(() =>
{
    list.AddRange([4, 5]);
    dictionary.Remove("a");
    set.SymmetricExceptWith(["x", "w"]);
});
Report("Collections commit", $"{string.Join(",", list)} {dictionary.Count} {string.Join(",", set.Order(StringComparer.Ordinal))}");

var order = new Order { Status = "new" };
Attempt(() => Rewind.Run(() =>
{
    order.Total = 30m;
    order.Status = "paid";
    order.Views = 1;
    throw new InvalidOperationException("declined");
}));
Report("Rewindable rolls back", $"{order.Total} {order.Status} {order.Views}");

Rewind.Run(() => order.Total = 45m);
Report("Rewindable commits", order.Total);

var history = new RewindHistory();
var raised = new List<string>();
var form = new Form();
form.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);
history.Run(scope =>
{
    form.Title = "One";
    form.Tags.Add("a");
    scope.OnCommitted(changes => Report("Changes", changes.Changes.Count));
});
history.Run(() => form.Tags.Clear());
history.Undo();
history.Undo();
Report("History undone", $"{form.Title ?? "none"} {form.Tags.Count}");
history.Redo();
Report("History redone", $"{form.Title} {string.Join(",", form.Tags)}");
Report("Notifications", string.Join(",", raised));

var pending = new RewindCell<int>(0);
using (new System.Transactions.TransactionScope())
{
    Rewind.Run(() => pending.Value = 1);
}

Report("Transaction rolled back", pending.Value);
using (var transaction = new System.Transactions.TransactionScope())
{
    Rewind.Run(() => pending.Value = 2);
    transaction.Complete();
}

Report("Transaction committed", pending.Value);

var files = Directory.CreateTempSubdirectory("rewind-aot-");
var settings = Path.Join(files.FullName, "settings.txt");
RewindFile.WriteAllText(settings, "v1");
Attempt(() => Rewind.Run(() =>
{
    RewindFile.WriteAllText(settings, "v2");
    throw new InvalidOperationException("declined");
}));
Report("File rolled back", File.ReadAllText(settings));
Rewind.Run(() =>
{
    RewindFile.Write(settings, stream => stream.Write("v3"u8));
    RewindFile.Copy(settings, Path.Join(files.FullName, "copy.txt"));
    RewindFile.Delete(Path.Join(files.FullName, "missing.txt"));
});
Report("File committed", $"{File.ReadAllText(settings)} {files.GetFiles().Length}");
files.Delete(recursive: true);

var flowing = new RewindCell<int>(0);
try
{
    await Rewind.RunAsync(async () =>
    {
        flowing.Value = 1;
        await Task.Yield();
        flowing.Value = 2;
        throw new InvalidOperationException("declined");
    });
}
catch (InvalidOperationException)
{
}

Report("Async rolled back", flowing.Value);
var flowed = await Rewind.RunAsync(async scope =>
{
    flowing.Value = 3;
    await Task.Yield();
    return Rewind.Current == scope ? flowing.Value : -1;
});
Report("Async committed", $"{flowed} {flowing.Value}");

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

[Rewindable]
internal sealed partial class Order
{
    public partial decimal Total { get; set; }

    public partial string Status { get; set; }

    [RewindIgnore]
    public partial int Views { get; set; }
}

[Rewindable]
internal sealed partial class Form : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public partial string? Title { get; set; }

    public RewindList<string> Tags { get; } = [];
}
