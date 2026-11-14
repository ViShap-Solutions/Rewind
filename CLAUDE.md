# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

Core, Testing and the meta-package target `net8.0`, `net9.0` and `net10.0` (`RewindTargetFrameworks` in
`Directory.Build.props`, D-51); the generator `netstandard2.0`; everything else `net10.0`. SDK 10.0.x (`global.json`);
the .NET 8 and 9 runtimes must be installed to run the tests under them. Package versions are central
(`Directory.Packages.props`; the benchmarks' competitors in `benchmarks/Directory.Packages.props`).

```powershell
dotnet restore Rewind.slnx
dotnet build Rewind.slnx --configuration Release
dotnet test tests/ViShap.Rewind.Core.Tests/ViShap.Rewind.Core.Tests.csproj
dotnet test tests/ViShap.Rewind.Generator.Tests/ViShap.Rewind.Generator.Tests.csproj

# Single test / class / folder
dotnet test tests/ViShap.Rewind.Core.Tests/ViShap.Rewind.Core.Tests.csproj --filter "FullyQualifiedName~Run_BlockThrows_RestoresCellAndRethrowsOriginal"
dotnet test tests/ViShap.Rewind.Core.Tests/ViShap.Rewind.Core.Tests.csproj --filter "FullyQualifiedName~NestingTests"
dotnet test tests/ViShap.Rewind.Core.Tests/ViShap.Rewind.Core.Tests.csproj --filter "FullyQualifiedName~.Effects."
dotnet test tests/ViShap.Rewind.Core.Tests/ViShap.Rewind.Core.Tests.csproj --framework net8.0   # one target framework

# Packages
dotnet pack Rewind.slnx --configuration Release --output artifacts -p:Version=0.0.0-local

# Benchmarks (Release only; usage in benchmarks/ViShap.Rewind.Benchmarks/README.md, plan in internal/Benchmark-Plan.md)
$bench = "benchmarks/ViShap.Rewind.Benchmarks"
dotnet run --project $bench -c Release -- --track A                 # Rewind alone: verify, every suite, cold start, soak, report
dotnet run --project $bench -c Release -- --track B                 # Rewind beside the alternatives (tier T1)
dotnet run --project $bench -c Release -- --track A --short         # the whole pipeline in minutes; never a baseline
dotnet run --project $bench -c Release -- --track A --filter a2     # stages a1, a2, a3, b1 or a glob over suite names
dotnet run --project $bench -c Release -- --track A --list          # what would run and where it would go; measures nothing
dotnet run --project $bench -c Release -- --verify                  # Track A cases and Track B probes and pairs, no timing
dotnet run --project $bench -c Release -- --verify --market         # Track B probes, tiers and pairs only
dotnet run --project $bench -c Release -- --report <run directory>  # regenerate report and charts; rebuild an interrupted run
dotnet run --project $bench -c Release -- --compare <baseline> <run> # delta with 99.9 % margins: comparison-*.md/.csv/.svg
dotnet run --project $bench -c Release -- --cold                    # cold start alone (--launches N)
dotnet run --project $bench -c Release -- --soak 5                  # sustained load alone, minutes
dotnet run --project $bench -c Release -- --manifest                # the environment record
dotnet run --project $bench -c Release -- --smoke                   # every benchmark once
dotnet run --project $bench -c Release -- --filter '*Nesting*'      # BenchmarkDotNet's switcher, publication job
```

A track run on a commit with its own tag, under the publication job and with no filter, is a baseline: it is moved to
`benchmarks/ViShap.Rewind.Benchmarks/Baselines/<tag>/` (Track B: `<tag>/market/`) and committed; anything else is a
measurement under `Measurements/`, git-ignored. Track A and Track B are run on the tag `v1.0.0` after its release, not
on pre-release tags (D-52). `--as <tag>` files a later commit under a tag whose `src/` it carries in code;
`--allow-dirty` lets a baseline be measured over uncommitted changes and records it. Ctrl+C or a `STOP` file in the
run directory ends a run after the current suite.

CI (`.github/workflows/ci.yml`, on every PR to and push on `main`, `release/**`, `support/**`, and by hand) has two
jobs: restore → build → both test projects (`.trx` uploaded; Core's tests under .NET 8, 9 and 10) → pack, on Linux
and on Windows; and Native AOT, which publishes `tests/ViShap.Rewind.AotConsumer` with native AOT on Linux for
`net10.0` and for `net8.0` (C# 13), fails on any `IL` warning, and diffs each output with the same program under the
JIT and the two with each other. CD (`cd.yml`) fires on `v*` tags: a stable `vX.Y.Z` must point exactly at `origin/main`
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

Current state: **B0–B13a closed, `v1.0.0-rc.2` published (targets `net8.0`, `net9.0`, `net10.0`, D-51); B13b
(real-world defects: single-phase commit D-53; notifications after rollback and of collections D-54; RewindFile
through symbolic links) gate holds —
then the owner tags `v1.0.0-rc.3`.** Every
v1 section of the contract is Built and the §18 checklist is complete (2026-11-13, `cea304d`). Core holds the engine of
contract §4–§7, the collections of §8, `ChangeSet`/notifications/`RewindHistory` of §10, joins an ambient
`System.Transactions` transaction (§11), binds file operations to scopes with `RewindFile` (§12), runs asynchronous
blocks with `Rewind.RunAsync` (§13), and reports metrics and traces under `RewindDiagnostics` (§15); the attributes and
the seam `ViShap.Rewind.Contracts`. The generator implements `[Rewindable]` partial properties and their
notifications; analyzers report RWD0001–RWD0006 and RWD1001–RWD1006. The Testing package holds `RewindVerify` (§14).
INV-01…INV-13 are pinned; the v1 audit is closed (`internal/audit/Audit-v1.0.0.md`); Track A has two runs on the old
harness; the benchmark harness (Track A and Track B) is rebuilt and its first publication runs are on the tag `v1.0.0`
after its release (D-52). CI builds and tests on Linux and Windows. After `rc.3`: only fixes until `v1.0.0`.

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
- `ViShap.Rewind.Testing` — `RewindVerify` for the consumer's tests (contract §14). Depends on Core; not in the
  meta-package; packed and published by CD. Tested in `Core.Tests/Verify/`, which references it.
- `ViShap.Rewind` — meta-package: references Core and Generator, ships no code.
- `tests/ViShap.Rewind.AotConsumer` — a console program over Core and the generator, run by the Native AOT job of CI;
  `net10.0` and `net8.0` at C# 13, the lowest language version `[Rewindable]` needs.
- `benchmarks/ViShap.Rewind.Benchmarks` — the benchmark harness: Track A suites in `Suites/`, Track B adapters and
  suites in `Market/`, runners, reports and charts; read-only over `src/` (`rewind_bencher`).
- Conditional compilation in Core is only for runtime differences under `net8.0` (`NET9_0_OR_GREATER`): the lock type
  of `TransactionParticipant` and the exception event of `Telemetry`.

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
  Collections notify (`INotifyCollectionChanged`, D-54): with no scope at once (a list with positions), in a scope one
  `Reset` when the outermost scope ends; `Engine/CollectionNotifications` (`ICollectionNotifier`) links them to entries.
- **First write per scope only.** Each scope has a unique epoch; a cell remembers the epoch of the scope that last
  recorded it. A nested scope records the cell again, so it can restore what it saw at its mark.
- **Savepoints are marks.** Rolling back a child undoes the entries above its mark, newest first; committing a
  child leaves its entries in place and hands its effect lists to the parent.
- **After the outermost commit:** notifications (`PropertyChanged` of notifying rewindable types, `Reset` of notifying
  collections), then `OnCommitted` observers with the `ChangeSet` (built only when one is registered), then
  `AfterCommit` effects.
- **After a rollback:** `Journal.RollbackTo` keeps every notifying entry whose value it restored; the outermost scope
  raises them (`Completion.NotifyRestored`) after the state is restored and before `AfterRollback`, and a commit raises
  those a nested rollback kept along with its own (D-54). A two-way binding that wrote the undone value shows the
  restored one.
- **Ambient transactions.** An outermost scope opened in an active `Transaction.Current` joins it: its commit is
  pending in the transaction's single `TransactionParticipant` until the outcome; its rollback rolls the transaction
  back. The participant enlists in nothing — it only subscribes to `TransactionCompleted` (D-53), so a single
  connection still commits in one phase (Npgsql would otherwise `PREPARE TRANSACTION`) — and completes the pending
  scopes there, throwing their failures only on the thread where the first scope joined (D-49). `Engine/Completion` runs the end of a commit or rollback for both paths.
- **History replays entries through the recording paths.** `RewindHistory.Undo` undoes a step's entries inside a new
  scope that records them, so that scope's change set is the redo step.
- **Diagnostics.** `Engine/Telemetry` holds the `Meter`, its five instruments and the `ActivitySource`; each report
  checks `Enabled`/`HasListeners` first. An outermost scope's activity lives in a `ConditionalWeakTable` while tracing.
  Each report is an inlined `Enabled`/`HasListeners` check, then a non-inlined call. A scope reports when it opens
  and once when its outcome is final: `RewindScope` for its own commit and rollback (`Telemetry.Closed` covers metrics
  and the activity), `TransactionParticipant` for a scope that waited for its transaction.
- **Fault injection (Testing).** `RewindVerify` arms its flow (`FaultInjection`, an `AsyncLocal` read only while a
  static counter says something is armed); an outermost scope opened in an armed flow gets an `InjectingJournal` (or a
  `GuardedJournal` with the injector) that calls `IFaultInjector` after every recording and effect registration, and
  wraps `AfterCommit`/`OnCommitted` to see whether they run. The operation's own scopes commit for real.
- **Files are effects.** `RewindFile` writes new content to a side file and renames it over the target at the call;
  in a scope the previous file is kept as `.rewind-bak`, a `CompensationEntry` puts it back on rollback and an
  `AfterCommit` effect deletes it. With no scope the operation is a plain atomic rename. It is the only I/O in Core.
- **Scope kinds.** A scope computes `_inFlow` once (asynchronous, or nested in an asynchronous scope; guarded journal).
  Outside a flow, `Open`/`Close`/`RollbackCore` only set `ScopeContext.SetCurrent`; everything about flows lives in
  the non-inlined `OpenInFlow`/`CloseInFlow` and `Suspend`/`Resume` (PERF-03, D-48).
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
