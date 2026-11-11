# Changes, notifications and history

When an outermost scope commits, the library knows exactly which tracked values it changed. Three features use
that: a `ChangeSet` delivered to whoever asks for it, `PropertyChanged` raised once for the committed state, and an
undo and redo history.

## Observing what a scope changed

`scope.OnCommitted(action)` registers an action that receives the `ChangeSet` of the outermost scope once it has
committed: every tracked value it and the scopes nested in it changed, each once, in the order of its first change.
Like `AfterCommit`, a registration made in a nested scope waits for the outermost commit and is discarded if an
enclosing scope rolls back. When no action is registered, no change set is built.

```csharp
using ViShap.Rewind;

var order = new Order { Status = "new" };
var discount = new RewindCell<decimal>(0m);

Rewind.Run(scope =>
{
    order.Status = "paid";
    order.Total = 90m;
    discount.Value = 10m;
    order.Lines.Add("book");
    scope.OnCommitted(changes =>
    {
        foreach (var change in changes.Changes)
        {
            Console.WriteLine($"{change.Member ?? change.Owner.GetType().Name}: {change.OldValue} -> {change.NewValue}");
        }
    });
});
// Status: new -> paid
// Total: 0 -> 90
// RewindCell`1: 0 -> 10
// RewindList`1:  ->

[Rewindable]
public partial class Order
{
    public partial string Status { get; set; }

    public partial decimal Total { get; set; }

    public RewindList<string> Lines { get; } = [];
}
```

Each `Change` has an `Owner`, a `Member`, an `OldValue` and a `NewValue`:

| Changed | `Owner` | `Member` | `OldValue`, `NewValue` |
|---|---|---|---|
| a property of a `[Rewindable]` class | the instance | the property name | the value when the scope opened, and when it committed |
| a `RewindCell<T>` | the cell | `null` | the same |
| a Rewind collection | the collection | `null` | `null`: the change states that the content changed |

A value written and brought back to where it started is not listed; `ChangeSet.IsEmpty` is `true` when nothing
changed.

## Change notifications

A `[Rewindable]` class that implements `INotifyPropertyChanged` — itself, or through a base such as an MVVM
`ObservableObject` — raises `PropertyChanged` for its tracked properties:

- inside a scope, once per changed property after the outermost scope commits, with the committed state visible and
  no scope open — never while the scope is open, never for a change that was rolled back;
- with no scope open, at once, when the value changes.

The generated code raises the notification through the class's `OnPropertyChanged(string)`, otherwise its
`OnPropertyChanged(PropertyChangedEventArgs)`, otherwise its own `PropertyChanged` event.

```csharp
using System.ComponentModel;
using ViShap.Rewind;

var form = new Form();
form.PropertyChanged += (_, e) => Console.WriteLine($"{e.PropertyName} = {form.Name}");

form.Name = "Ana";                          // Name = Ana

try
{
    Rewind.Run(() =>
    {
        form.Name = "Bob";
        throw new InvalidOperationException("Rejected");
    });
}
catch (InvalidOperationException)
{
}

Rewind.Run(() =>
{
    form.Name = "Eva";
    form.Name = "Kim";
});                                         // Name = Kim

[Rewindable]
public partial class Form : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public partial string Name { get; set; }
}
```

A failing handler does not undo the committed state and does not stop the other notifications; the commit then
throws `RewindEffectException`, as for a failing `AfterCommit` effect. Notifications run first, then the
`OnCommitted` actions, then the `AfterCommit` effects.

## Undo and redo

`RewindHistory` records the operations run through it. `history.Run(...)` runs an operation in a scope, as
`Rewind.Run` does; once it commits, its changes become a step. `Undo()` reverts the last step and `Redo()` reapplies
it, each as a scope of its own, so notifications follow them as for any commit.

```csharp
using ViShap.Rewind;

var note = new Note { Title = "Draft" };
var history = new RewindHistory();

history.Run(() =>
{
    note.Title = "Shopping";
    note.Items.Add("milk");
});
history.Run(() => note.Items.Add("bread"));

history.Undo();
Console.WriteLine(string.Join(",", note.Items)); // milk
history.Undo();
Console.WriteLine($"{note.Title} {note.Items.Count}"); // Draft 0
history.Redo();
Console.WriteLine($"{note.Title} {string.Join(",", note.Items)}"); // Shopping milk

[Rewindable]
public partial class Note
{
    public partial string Title { get; set; }

    public RewindList<string> Items { get; } = [];
}
```

- A step covers cells, properties of rewindable classes and every mutation of Rewind collections, `Clear` included.
- A new `Run` drops the steps to redo; a step that changed nothing is not recorded. `CanUndo` and `CanRedo` tell
  whether there is something to do; `Undo()` or `Redo()` with nothing to do throws `InvalidOperationException`.
  `Clear()` forgets every step.
- `history.Run` inside an open scope records its step when the outermost scope commits, and nothing if it rolls back.
  `Undo()` and `Redo()` themselves run only with no scope open; otherwise they throw `RewindScopeException`.
- If reverting or reapplying throws, the scope rolls back: the state is as it was before the call, and the step stays.
- Undo and redo restore tracked state only. The compensations and the deferred effects of the operation do not run
  again: an e-mail sent after the commit is not unsent by `Undo()`.
- A history assumes that what it recorded changes only through it between a step and its undo.
