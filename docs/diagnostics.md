# Diagnostics

The compiler reports, as diagnostics with a stable `RWDnnnn` identifier, the mistakes that would make a rollback
silently miss something. An identifier never changes meaning. A warning can be suppressed like any other — with
`#pragma warning disable`, `[SuppressMessage]` or a severity in `.editorconfig`.

| Id | Severity | Meaning |
|---|---|---|
| [RWD0001](#rwd0001) | Error | a `[Rewindable]` class, or a type it is nested in, is not `partial` |
| [RWD0002](#rwd0002) | Warning | a writable auto-property of a `[Rewindable]` class is not `partial`, so a rollback does not restore it |
| [RWD0003](#rwd0003) | Error | a `partial` property has an `init` accessor or returns by reference |
| [RWD0004](#rwd0004) | Warning | a `partial` property has a mutable reference type: only the reference is tracked |
| [RWD0005](#rwd0005) | Error | a `partial` property is `static`, an indexer, or has no `set` accessor |
| [RWD1001](#rwd1001) | Warning | the scope returned by `Rewind.Begin()` is never disposed |
| [RWD1002](#rwd1002) | Warning | a call that changes something outside the process is made directly inside a scope |
| [RWD1003](#rwd1003) | Error | an asynchronous block is passed to `Rewind.Run` |
| [RWD1004](#rwd1004) | Warning | a `RewindCell<T>` holds a mutable reference type: only the reference is tracked |
| [RWD1005](#rwd1005) | Warning | a field of a `[Rewindable]` type is written inside a scope, and a rollback does not restore it |

## RWD0001

**A `[Rewindable]` class must be partial** (error). The generator writes the implementation of the class's
`partial` properties in another part of the class, which it can do only if the class — and every type it is nested
in — is `partial`. Nothing is generated until it is. Add `partial` to the type the diagnostic points at.

## RWD0002

**A writable property is not rolled back** (warning). The property has an automatic `set` accessor but is not
`partial`, so it is plain storage: a rollback leaves it as the block left it. Declare it `partial` to have it
tracked. If it is deliberately untracked — a cache, a counter for diagnostics — mark it `[RewindIgnore]`.

A property whose accessors have a body is not reported, since it may store its value in tracked state such as a
`RewindCell<T>`. A collection property of type `RewindList<T>`, `RewindDictionary<TKey, TValue>` or `RewindSet<T>`
is not reported either: the collection tracks its own content.

## RWD0003

**A partial property has an init accessor or a ref return** (error). A tracked property has exactly a `get` and a
`set` accessor. An `init` accessor runs only during initialisation, and a `ref` return would let a caller write the
storage without going through the setter. Use `set`, or make the property non-partial if it is not meant to be
tracked.

## RWD0004

**Only the reference is tracked** (warning). The property's type is a mutable reference type — an array, a
collection such as `List<T>`, or a class with public setters or fields. A rollback restores which object the
property refers to, but not changes made inside that object. Prefer an immutable type (a `record` with `init`
properties, `string`, a type of `System.Collections.Immutable`), a Rewind collection, or a `[Rewindable]` type,
whose own properties are tracked. If only replacing the reference matters, suppress the warning.

## RWD0005

**A partial property cannot be tracked** (error). The generator implements instance properties with a `get` and a
`set` accessor. A `static` property, an indexer, or a property without a `set` accessor has no implementation it can
write. Make the property an instance property with `get` and `set`, or implement it yourself.

## RWD1001

**The scope is never disposed** (warning). The result of `Rewind.Begin()` is discarded, or held by a local that is
never disposed and never handed over (returned, passed or stored). Such a scope stays the current scope of the
thread: every later write is recorded into it, and nothing ever commits or rolls it back. Declare it with `using` —
`using var scope = Rewind.Begin();` — and call `scope.Commit()` to keep the changes; the code fix does the first.

A scope declared with `using` and never committed is not reported: disposing it rolls everything back, which is a
deliberate way to try a change and undo it.

## RWD1002

**An external effect is called directly inside a scope** (warning). The call — writing, moving or deleting a file,
an HTTP request, sending mail or socket data, starting a process — changes something a rollback cannot undo. If
the scope then rolls back, the effect has happened for a change that was not kept. Register it instead:
`scope.AfterCommit(() => …)` runs it only once the change is kept, and `scope.Effect(do, undo)` runs it at once with
a compensation for a rollback. The code fix moves the call into `AfterCommit` when a scope variable is in reach.

Reading a file is not reported. Code in a block passed to `AfterCommit`, `AfterRollback` or `Effect`, and code in a
method called from the scope, is not reported.

## RWD1003

**An asynchronous block is passed to Rewind.Run** (error). `Rewind.Run` runs its block synchronously: an `async`
block returns at its first `await`, the scope closes, and the rest of the block runs outside it, untracked. A block
returning a task is also refused at run time. Run the synchronous part of the work in the scope.

## RWD1004

**Only the reference is tracked** (warning). A `RewindCell<T>` is created for a mutable reference type — an array,
a collection such as `List<T>`, or a class with public setters or fields. A rollback restores which object the cell
refers to, not changes made inside that object. Use an immutable type, a Rewind collection, or a `[Rewindable]`
type; if only replacing the reference matters, suppress the warning.

## RWD1005

**An untracked field is written inside a scope** (warning). In a `[Rewindable]` type, a field of `this` is
assigned, incremented or decremented inside a scope. A field is plain storage: a rollback restores the type's
tracked properties but leaves the field as the block left it. Turn the field into a `partial` property, or use a
`RewindCell<T>` for it. Writes in a block passed to `AfterCommit`, `AfterRollback` or `Effect` are not reported.

