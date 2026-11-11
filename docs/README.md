# ViShap.Rewind documentation

One page per subject. Every `csharp` block on these pages is a complete example that compiles against the current
code.

| Page | Subject |
|---|---|
| getting-started.md | Installing, the first scope, what is and is not tracked |
| scopes.md | `Rewind.Run`, `Rewind.Begin`, nesting, commit and rollback |
| effects.md | `AfterCommit`, `AfterRollback`, `Effect`: binding external work to the outcome |
| exceptions.md | What each exception means and what state it leaves |
| rewindable-types.md | `[Rewindable]` classes: tracked properties with ordinary syntax, `[RewindIgnore]`, diagnostics |
| changes-and-history.md | `ChangeSet` and `OnCommitted`, `PropertyChanged` after the commit, undo and redo with `RewindHistory` |
| diagnostics.md | Every `RWDnnnn` diagnostic: what it means and how to fix it |
| collections.md | `RewindList<T>`, `RewindDictionary<TKey, TValue>`, `RewindSet<T>`: rolling back mutations, `Clear`, enumeration |

The pages are written as the stages that build each subject close.
