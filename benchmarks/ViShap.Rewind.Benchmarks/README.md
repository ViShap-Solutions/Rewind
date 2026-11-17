# ViShap.Rewind benchmarks

The harness that records what every mechanism of Rewind costs, per version, and compares Rewind with the alternatives
that do the same job. It measures the library as it ships and never changes it.

- **Track A** — Rewind alone, against plain code doing the same work with no rollback.
- **Track B** — Rewind beside the alternatives, inside tier T1: undo in-memory changes on one thread when a block
  fails. The roster is a plain-code floor, a hand-written memento, `ImmutableInterlocked`, Shielded, STMSharp and
  LanguageExt, each in its own documented best mode.

## One command per track

```text
dotnet run --project benchmarks/ViShap.Rewind.Benchmarks -c Release -- --track A
dotnet run --project benchmarks/ViShap.Rewind.Benchmarks -c Release -- --track B
```

A track verifies before it times anything, runs its suites in order, writes every artifact into the run's own
directory and generates the report. Nothing in the middle is interactive. Ctrl+C, or a file named `STOP` in the run's
directory, finishes the current suite and writes what was measured; a second Ctrl+C stops at once.

| Flag | Effect |
|---|---|
| `--short` | One warmup and three iterations of 50 ms: proves the pipeline in minutes; never a baseline |
| `--filter a1,*Scaling*` | Only the suites whose stage (`a1`, `a2`, `a3`, `b1`) or type name matches; never a baseline |
| `--list` | Prints the suites, the destination and the kind of record, and measures nothing |
| `--soak 10` | Minutes of sustained load at the end of Track A (default 5; `0` skips it) |
| `--launches 20` | Processes per cold-start scenario (default 20) |
| `--as v1.0.0` | Files the run under a tag from a later commit whose `src/` is that tag's in code |
| `--allow-dirty` | Lets a baseline be measured over uncommitted changes, and records that it was |

## Where a run goes

A run on a commit that carries a tag of its own, under the publication job, with no filter, is a **baseline**: it is
measured under `Measurements/` and moved to `Baselines/<tag>/` (Track B: `Baselines/<tag>/market/`) only once every
suite has run. A baseline is never overwritten. Anything else is a **measurement** under
`Measurements/<git describe>-<time>/`, which is not committed and says in `scope.md` what it did not measure.

| File | What it holds |
|---|---|
| `report.html`, `report.md` | The report: identity, caveats, environment, verification, every table, the charts, the noisy cells |
| `charts/*.svg` | The charts, drawn from the files below and nothing else |
| `results.csv` | Every timed cell: mean, standard error, median, P95, allocation, GC counts, iterations |
| `memory.csv` | Allocation per cell, without timings |
| `run.json` | What the run was: kind, tag, revision, job, filter, suites run and not run, how it ended |
| `environment.json` | Machine, runtime, GC, revision, the libraries as loaded, every pinned package and its latest version |
| `manifest.md`, `reproduction.md`, `scope.md` | The summary, the commands that reproduce the run, what a measurement left out |
| `verification.csv` | Track A: every benchmark invoked once, no scope left open |
| `roster.csv`, `capabilities.csv`, `tiers.csv`, `market-verification.csv`, `workloads.csv`, `unmeasured.csv` | Track B: the adapters with their rationale, the probes, the tiers, the verified pairs |
| `cold-start.csv`, `soak.csv` | Track A: first use in a fresh process; sustained load |
| `results.zip` | BenchmarkDotNet's own export, packed |

## Other modes

```text
-- --verify                  every Track A case and every Track B probe and pair, no timing
-- --verify --market         Track B only
-- --report <run directory>  regenerates the report and charts from the raw files (rebuilds an interrupted run)
-- --compare <baseline> <run> cell-by-cell delta with margins: comparison-<baseline>.md, .csv and .svg
-- --manifest                the environment record alone
-- --cold                    cold start alone
-- --soak 5                  sustained load alone
-- --smoke                   every benchmark once, to prove each suite starts
-- --filter '*Nesting*'      BenchmarkDotNet's own switcher, publication job
```

`reproduction/check.sh` replays a run's instructions in a clean SDK container and compares what does not depend on
the machine.

## Reading the numbers

Every mean carries its 99.9 % margin of error. Two cells whose intervals overlap are no measurable difference, and a
ratio is never read without the margins beside it. Timings belong to the machine in `environment.json`; allocation,
the verification and the probes do not. Track B tables keep the roster's order and are never sorted by a number.
