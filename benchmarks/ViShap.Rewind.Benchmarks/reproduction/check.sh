#!/usr/bin/env bash
#
# Proves that a run's reproduction instructions are complete, by carrying them out in a container that starts from
# the vendor .NET SDK image and holds nothing from the machine that took the run. A forgotten prerequisite fails here,
# because there is nothing in the container to satisfy it.
#
# What it checks, and what it deliberately does not:
#
#   checked      the documented sequence restores and builds from a clean clone
#   checked      every Track A benchmark is invoked once and leaves no scope open
#   checked      every Track B probe agrees with its adapter's declaration, and every timed pair does its job
#   checked      the probes are identical to the ones committed with the run (capabilities.csv), when one is given
#   checked      every benchmark suite starts and completes one invocation
#   not checked  timings and throughput — those belong to the recorded machine and differ here
#
# Every step is judged by what it printed, not by its exit code: the benchmark switcher answers an argument it does
# not recognize by printing its help and exiting zero, so a mode that does not exist at the checked revision would
# otherwise pass as a mode that ran.
#
# Launch from the repository root.
#
#   PowerShell:
#     docker run --rm -v "${PWD}:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0 `
#         bash /src/benchmarks/ViShap.Rewind.Benchmarks/reproduction/check.sh <ref> [run-path]
#
#   bash:
#     docker run --rm -v "$PWD:/src:ro" mcr.microsoft.com/dotnet/sdk:10.0 \
#         bash /src/benchmarks/ViShap.Rewind.Benchmarks/reproduction/check.sh <ref> [run-path]
#
#   Git Bash on Windows rewrites container paths; prefix the command with MSYS_NO_PATHCONV=1 there.
#
#   <ref>       the tag or commit the run was taken from, e.g. v1.0.0
#   [run-path]  repository-relative path of a committed run; a Track B run (…/market) has its probes compared
#
# The clone is taken from the mounted repository, so only committed content reaches the container.

set -euo pipefail

REF="${1:?usage: check.sh <ref> [run-path]}"
RUN="${2:-}"

PROJECT="benchmarks/ViShap.Rewind.Benchmarks"
CLONE="/work/clone"
LOGS="/work/logs"
STARTED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

step() {
    printf '\n=== %s\n' "$1"
}

fail() {
    printf '\nreproduction check FAILED: %s\n' "$1" >&2
    exit 1
}

harness() {
    local name="$1"
    shift
    dotnet run --project "$PROJECT" --configuration Release --no-build -- "$@" | tee "$LOGS/$name.log"
}

mkdir -p "$LOGS"

step "environment"
dotnet --info | sed -n '1,12p'
uname -srm

step "clone $REF"
git config --global --add safe.directory '*'
rm -rf "$CLONE"
git clone --quiet /src "$CLONE"
cd "$CLONE"
git checkout --quiet "$REF"
COMMIT="$(git rev-parse HEAD)"
echo "commit $COMMIT"

step "restore and build"
dotnet restore Rewind.slnx
dotnet build Rewind.slnx --configuration Release --no-restore

step "verify — Track A cases and Track B probes and pairs"
harness verify --verify
CASES_LINE="$(grep -E '^[0-9]+ cases, [0-9]+ failed\.' "$LOGS/verify.log" || true)"
[ -n "$CASES_LINE" ] || fail "--verify printed no Track A result; the mode does not exist at $REF"
CASES_FAILED="$(sed -E 's/^[0-9]+ cases, ([0-9]+) failed\..*/\1/' <<<"$CASES_LINE")"
[ "$CASES_FAILED" -eq 0 ] || fail "$CASES_LINE"

MARKET_LINE="$(grep -E '^[0-9]+ probes, [0-9]+ pairs, [0-9]+ failed\.' "$LOGS/verify.log" || true)"
[ -n "$MARKET_LINE" ] || fail "--verify printed no Track B result; the mode does not exist at $REF"
MARKET_FAILED="$(sed -E 's/^[0-9]+ probes, [0-9]+ pairs, ([0-9]+) failed\..*/\1/' <<<"$MARKET_LINE")"
[ "$MARKET_FAILED" -eq 0 ] || fail "$MARKET_LINE"

PROBES_VERDICT="not compared — no Track B run was given"
if [ -n "$RUN" ] && [ -f "/src/$RUN/capabilities.csv" ]; then
    # The probes are properties of the libraries, not of the machine: a difference is a change in a library or in an
    # adapter, never noise.
    diff "/src/$RUN/capabilities.csv" "$CLONE/$PROJECT/BenchmarkDotNet.Artifacts/market/capabilities.csv" \
        || fail "the probes differ from the ones committed with $RUN"
    PROBES_VERDICT="identical to $RUN/capabilities.csv"
fi
echo "$PROBES_VERDICT"

step "smoke — every suite starts"
harness smoke --smoke
SMOKE_LINE="$(grep -E '^Smoke: [0-9]+ benchmarks, [0-9]+ failed\.' "$LOGS/smoke.log" || true)"
[ -n "$SMOKE_LINE" ] || fail "--smoke printed no result; the mode does not exist at $REF"
SMOKE_COUNT="$(sed -E 's/^Smoke: ([0-9]+) benchmarks.*/\1/' <<<"$SMOKE_LINE")"
SMOKE_FAILED="$(sed -E 's/.*, ([0-9]+) failed\.$/\1/' <<<"$SMOKE_LINE")"
[ "$SMOKE_COUNT" -gt 0 ] || fail "--smoke ran no benchmarks at all"
[ "$SMOKE_FAILED" -eq 0 ] || fail "$SMOKE_FAILED of $SMOKE_COUNT benchmarks failed to run"

step "verdict"
cat <<VERDICT
reproduction check passed
  started            $STARTED_AT
  finished           $(date -u +%Y-%m-%dT%H:%M:%SZ)
  ref                $REF
  commit             $COMMIT
  container          $(uname -srm)
  sdk                $(dotnet --version)
  track A cases      $CASES_LINE
  track B            $MARKET_LINE
  probes             $PROBES_VERDICT
  suites             $SMOKE_COUNT benchmarks, $SMOKE_FAILED failed
  timings            not compared — they belong to the recorded machine
VERDICT
