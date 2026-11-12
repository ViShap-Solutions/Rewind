# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

Targets .NET 10 (`net10.0`), SDK 10.0.x (`global.json`). Package versions are central (`Directory.Packages.props`).

```powershell
dotnet restore Rewind.slnx
dotnet build Rewind.slnx --configuration Release
dotnet test tests/ViShap.Rewind.Core.Tests/ViShap.Rewind.Core.Tests.csproj
dotnet test tests/ViShap.Rewind.Generator.Tests/ViShap.Rewind.Generator.Tests.csproj

# Single test / class / folder
dotnet test tests/ViShap.Rewind.Core.Tests/ViShap.Rewind.Core.Tests.csproj --filter "FullyQualifiedName~Run_BlockThrows_RestoresCellAndRethrowsOriginal"
dotnet test tests/ViShap.Rewind.Core.Tests/ViShap.Rewind.Core.Tests.csproj --filter "FullyQualifiedName~NestingTests"
dotnet test tests/ViShap.Rewind.Core.Tests/ViShap.Rewind.Core.Tests.csproj --filter "FullyQualifiedName~.Effects."

# Packages
dotnet pack Rewind.slnx --configuration Release --output artifacts -p:Version=0.0.0-local

# Benchmarks (BenchmarkDotNet; Release only)
dotnet run --project benchmarks/ViShap.Rewind.Benchmarks --configuration Release -- --filter '*'
```

CI (`.github/workflows/ci.yml`, on every PR to and push on `main`, `release/**`, `support/**`, and by hand) has two
jobs: restore → build → both test projects (`.trx` uploaded) → pack, on Linux and on Windows; and Native AOT, which
publishes `tests/ViShap.Rewind.AotConsumer` with native AOT on Linux, fails on any `IL` warning, and diffs its output with the
same program under the JIT. CD (`cd.yml`) fires on `v*` tags: a stable `vX.Y.Z` must point exactly at `origin/main`
HEAD; a pre-release `vX.Y.Z-alpha.N`, `-beta.N` or `-rc.N` must point at a commit on an origin `release/*` branch
and is refused once `vX.Y.Z` exists. Its first job builds, tests and packs Core, Generator and the meta-package with
`-p:Version=<tag minus v>`, then checks the three packages and Core's `.snupkg`, each nuspec's version, icon and
repository, the meta-package's two dependencies and the generator's `analyzers/dotnet/cs` path. Its second job runs
in the `production` environment (owner approval), signs in to nuget.org by Trusted Publishing (`NuGet/login`, secret
`NUGET_USER` = the nuget.org account name; no API key), pushes, and creates the GitHub release. Version comes solely
from the tag.

Branches, tags, the release cycle and SemVer are defined in `internal/Development-Workflow.md` (Russian). It binds
the owner, every agent and every skill; follow it for anything about how work moves through the repository.

## Where the authoritative information lives

`internal/` is **git-ignored**: it exists on the owner's machine and in Claude Code sessions, never in the public
repository. Nothing committed — `src/`, `tests/`, `docs/`, the package READMEs, XML documentation — cites it.

`docs/` is the consumer documentation, one page per subject. Every `csharp` block in `docs/`, `README.md` and the
package READMEs (`src/*/*-README.md`) is a complete example that compiles against the current code;
`Api/DocumentationExampleTests` compiles each one and runs those with top-level statements. A change to the public
surface of Core changes contract §3.1 and `tests/ViShap.Rewind.Core.Tests/Api/PublicSurface.Core.txt` together. No page carries an adjective about performance
(`internal/Benchmark-Plan.md` §9).

Every file under `internal/` states its class at its head (`internal/README.md`): **normative**, **plan**,
**operational**, **historical**.

- `internal/System-Contract.md` — *normative.* **The source of truth.** Purpose and non-goals (§1), architecture,
  packages and namespaces (§2), public surface (§3), scope semantics (§4), tracked state (§5), effects (§6),
  exceptions (§7), collections (§8), generator and diagnostics (§9), ChangeSet/notifications/history (§10),
  System.Transactions (§11), `RewindFile` (§12), asynchronous scopes (§13), Testing package (§14), observability
  (§15), performance obligations (§16), invariants INV-01…INV-13 (§17), release checklist (§18). Each section is
  **Built** or **Designed (Bn)**. Read the relevant section before changing behaviour; update it in the same change.
- `internal/Decisions.md` — *normative* (Russian). The owner's decision record D-01…: what was chosen, what was
  rejected and why. The contract is written from it; if they disagree, report it.
- `internal/Build-Plan.md` — *plan.* Stages B0–B13 to `v1.0.0`, the Progress table, the owner questions Q-nn still
  open, and each stage's gate.
- `internal/QA-Plan.md` — *plan.* Test layout and checkpoints per stage, each naming its contract clause and test.
- `internal/Benchmark-Plan.md` — *plan.* Roster, tiers, workloads, rules, claim gate.
- `internal/ideas/` — *plan.* Features deliberately not in v1 (Concurrent STM, MVCC, `[Atomic]`, EF Core, commute,
  point of no return).
- `internal/performance/` — *plan.* Optimization proposals from measurements, for the owner to decide.
- `internal/Development-Workflow.md` — *operational* (Russian). Never edited by an agent unless the owner asks.

Current state: **B0–B8 closed, `v1.0.0-alpha.1` published; B9 gate holds — awaiting commit.** Core holds the engine
of contract §4–§7, the collections of §8, `ChangeSet`/notifications/`RewindHistory` of §10, joins an ambient
`System.Transactions` transaction (§11), binds file operations to scopes with `RewindFile` (§12), and runs
asynchronous blocks with `Rewind.RunAsync` (§13); the attributes and the seam `ViShap.Rewind.Contracts`. The generator
implements `[Rewindable]` partial properties and their notifications; analyzers report RWD0001–RWD0006 and
RWD1001–RWD1006. Every QA checkpoint of B1–B9 has its test and INV-01…INV-13 are pinned. CI builds and tests on Linux
and Windows. The Testing project has no code yet. Next: B10 (Testing package).

## Projects

- `ViShap.Rewind.Core` — everything that runs. No package dependency. `InternalsVisibleTo` the core tests, the
  Testing package and the benchmarks (in the `.csproj`).
- `ViShap.Rewind.Generator` — Roslyn incremental generator and analyzers, `netstandard2.0`, packed to
  `analyzers/dotnet/cs`. Runs only in the compiler; no shipped assembly references it. `RewindPlanner` holds every
  rule; `RewindGenerator` emits through `RewindEmitter` from equatable `Model/` plans; `RewindableAnalyzer` reports
  `RWD0001`… from the same planner (a generator-reported diagnostic cannot be suppressed by `#pragma`). `RWD1001`…
  are reported by one analyzer per rule in `Analyzers/`, sharing `ScopeRegion` (what is inside a scope) and
  `TypeRules`. Release tracking in `AnalyzerReleases.*.md`; snapshots of emitted code in
  `tests/ViShap.Rewind.Generator.Tests/Snapshots/`, reviewed, never refreshed to make a test pass.
- `ViShap.Rewind.Generator.CodeFixes` — the code fixes, `netstandard2.0`, the only assembly referencing Roslyn
  Workspaces; not packable itself, packed into the generator package's `analyzers/dotnet/cs`.
- `ViShap.Rewind.Testing` — tools for the consumer's tests (contract §14). Depends on Core; not in the
  meta-package; packed by CD from B10.
- `ViShap.Rewind` — meta-package: references Core and Generator, ships no code.
- `tests/ViShap.Rewind.AotConsumer` — a console program over Core and the generator, run by the Native AOT job of CI.

**Namespaces do not follow folders or assemblies.** The whole consumer API sits in the bare `ViShap.Rewind`
namespace so consumers need one `using` — including what lives in `Exceptions/` and, later, collections and
attributes. The seam generated code calls is `ViShap.Rewind.Contracts` (from B4). Internal machinery is
`ViShap.Rewind.Engine`, imported everywhere in Core by `GlobalUsings.cs`. Every public type of Core is prefixed
`Rewind` or named after a contract concept (`ChangeSet`), so nothing collides with the BCL.

The entry class `Rewind` shares its name with the namespace's last segment (`Decisions.md` D-14). Inside this
repository that resolves fine; generated code must always write `global::ViShap.Rewind.Rewind`.

## Architecture

Documented normatively in `internal/System-Contract.md` §2. Layers, top to bottom:

```text
Rewind (static)        Run and RunAsync overloads, Begin, Current
RewindScope            boundary: nesting, commit, rollback, effect registration, thread or flow affinity
Tracked state          RewindCell<T>; generated partial properties (B4); RewindList/Dictionary/Set (B3)
Engine (internal)      ScopeContext: [ThreadStatic] innermost scope, mirrored from an AsyncLocal for asynchronous
                       scopes; process-wide epoch counter
                       Journal: undo log shared by an outermost scope and its children; a child is a mark
                       JournalEntry: CellEntry<T>, PropertyEntry<TOwner, T>, collection entries, CompensationEntry;
                       each undoes itself, undoes itself through the recording paths (history), and describes
                       its change (ChangeSet)
```

Key mechanics:

- **Undo log, not redo log.** A write goes in place; the previous value goes to the journal. Reads cost nothing.
- **Collections record inverses.** Every mutation of a Rewind collection in a scope records its inverse operation;
  `Clear` moves the storage itself into the journal. A version counter invalidates enumerators on mutation and undo.
- **First write per scope only.** Each scope has a unique epoch; a cell remembers the epoch of the scope that last
  recorded it. A nested scope records the cell again, so it can restore what it saw at its mark.
- **Savepoints are marks.** Rolling back a child undoes the entries above its mark, newest first; committing a
  child leaves its entries in place and hands its effect lists to the parent.
- **After the outermost commit:** notifications (`PropertyChanged` of notifying rewindable types), then
  `OnCommitted` observers with the `ChangeSet` (built only when one is registered), then `AfterCommit` effects.
- **Ambient transactions.** An outermost scope opened in an active `Transaction.Current` joins it: its commit is
  pending in the transaction's single `TransactionParticipant` (volatile enlistment) until the outcome; its rollback
  rolls the transaction back. `Engine/Completion` runs the end of a commit or rollback for both paths.
- **History replays entries through the recording paths.** `RewindHistory.Undo` undoes a step's entries inside a new
  scope that records them, so that scope's change set is the redo step.
- **Files are effects.** `RewindFile` writes new content to a side file and renames it over the target at the call;
  in a scope the previous file is kept as `.rewind-bak`, a `CompensationEntry` puts it back on rollback and an
  `AfterCommit` effect deletes it. With no scope the operation is a plain atomic rename. It is the only I/O in Core.
- **Asynchronous scopes.** `RunAsync` holds its scope in an `AsyncLocal` whose change callback copies it into the
  thread-static current scope whenever an execution context takes over a thread; a synchronous scope it displaces is
  kept on a per-thread stack (`t_displaced`) and restored when its flow returns. Writes, and opening and closing a
  synchronous scope, touch only the thread-static field.
  The journal of an asynchronous outermost scope is guarded: it knows its innermost open scope (`Top`) and refuses a
  recording through any other scope or during another recording; a closed scope's epoch is -1, so a late write
  records and is refused.
- **No current scope during rollback and effects.** `ScopeContext.Suspend` clears the current scope (and the flow's,
  for an asynchronous chain) while entries are undone and while `AfterCommit`/`AfterRollback` run, so writes there
  are plain writes; `Resume` restores it.
- **`Run` refuses awaitable results** before running the block (`AwaitableShape<T>`).
- **Managed scopes.** A scope opened by `Run` refuses `Commit`/`Dispose` from its callback; `Run` closes it, and
  rolls back any scope the callback leaked.

Dependencies point strictly downward; tracked state reaches the engine only through `ScopeContext.Current`.

## Conventions

- Tests are xUnit, `[Fact]`-based, named `Method_Scenario_Expectation`, in the folders of `internal/QA-Plan.md` §2,
  shared types in `Fixtures/`. No test depends on culture, time zone, wall-clock time or order. Code that must run
  on another thread uses a dedicated `Thread`, never `Task.Run` + wait (it may inline).
- Every public member has XML documentation for the NuGet consumer reading it on hover: what it does, what it
  takes, what it returns, which exception it raises. It never cites `internal/` and never records history.
- Exception constructors keep the inner exception on the same line as the message.
- Comments describe what the code does. Nothing in `src/` or `tests/` addresses the reader personally or records
  history — no "note:", no "before the fix", no stage names, no decision ids. Reasoning belongs in `internal/`.
- No adjective about performance in any README, package description, XML documentation or `docs/` page.
- The repository owner makes every commit. Never commit, push, open a PR or set a `v*` tag. Leave finished work in
  the working tree and report the changed paths.
- At a natural commit point — a stage gate held, a defect fixed, a session wrapped up — end the report with a
  ready commit message in English: one subject line, optionally a short clause after a dash naming the
  consequence. No body, no bullet list, no trailer.
- Talk to the owner in Russian.
