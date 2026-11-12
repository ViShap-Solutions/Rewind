# ViShap.Rewind documentation

One page per subject. Every `csharp` block on these pages is a complete example that compiles against the current
code, and those with top-level statements also run.

| Page | Subject |
|---|---|
| [getting-started.md](getting-started.md) | Installing, the first scope, what is tracked, what Rewind does not do |
| [scopes.md](scopes.md) | `Rewind.Run`, `Rewind.Begin`, nesting, commit and rollback, threads |
| [effects.md](effects.md) | `AfterCommit`, `AfterRollback`, `Effect`: binding external work to the outcome |
| [exceptions.md](exceptions.md) | What each exception means and what state it leaves |
| [rewindable-types.md](rewindable-types.md) | `[Rewindable]` classes: tracked properties with ordinary syntax, `[RewindIgnore]` |
| [collections.md](collections.md) | `RewindList<T>`, `RewindDictionary<TKey, TValue>`, `RewindSet<T>`: rolling back mutations, `Clear`, enumeration |
| [changes-and-history.md](changes-and-history.md) | `ChangeSet` and `OnCommitted`, `PropertyChanged` after the commit, undo and redo with `RewindHistory` |
| [transactions.md](transactions.md) | Following the outcome of an ambient `System.Transactions` transaction |
| [files.md](files.md) | `RewindFile`: writing, copying and deleting files that are put back on rollback |
| [async.md](async.md) | `Rewind.RunAsync`: a scope that follows an asynchronous block through its awaits |
| [observability.md](observability.md) | Metrics and traces under `RewindDiagnostics` |
| [testing.md](testing.md) | `ViShap.Rewind.Testing`: proving an operation leaves no trace at every failure point |
| [diagnostics.md](diagnostics.md) | Every `RWDnnnn` diagnostic: what it means and how to fix it |
