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
