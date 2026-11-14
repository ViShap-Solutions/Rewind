using System.Globalization;

namespace ViShap.Rewind.Benchmarks.Environment;

/// <summary>What kind of record a run produces.</summary>
internal enum RunKind
{
    /// <summary>
    /// Every suite of the track, on a tagged commit, under the publication job: the frozen record of a tagged
    /// revision, never rewritten.
    /// </summary>
    Baseline,

    /// <summary>
    /// Anything else: an untagged commit, a narrowed filter, a shortened job or a run that did not finish. It carries
    /// only what it measured and says so, so it can never be read as a baseline.
    /// </summary>
    Measurement,
}

/// <summary>
/// Where a run writes, and why. The decision is made from git and from the run's own shape, never from a flag that
/// names a version: a baseline signed with the wrong version is worse than a missing one.
/// </summary>
/// <remarks>
/// A run writes into <c>WorkingDirectory</c>. A measurement's working directory is its directory; a baseline is
/// measured under <c>Measurements/</c> and moved to <c>Baselines/</c> only once every suite has run
/// (<see cref="Settle"/>), so a run that stops early never leaves a partial record where a baseline belongs.
/// </remarks>
internal sealed record RunTarget(
    RunKind Kind,
    string Directory,
    string WorkingDirectory,
    string Label,
    string? Tag,
    string Describe,
    string Reason,
    bool Dirty,
    bool AllowDirty)
{
    /// <summary>
    /// The baselines directory as git names it from the repository's top. <see cref="Dirty"/> leaves it out: a
    /// baseline Track A wrote moments ago is this harness's output, not a change to what is measured.
    /// </summary>
    private const string BaselinesPathspec = Paths.ProjectName + "/Baselines";

    internal bool IsBaseline => Kind == RunKind.Baseline;

    /// <summary>When the run measures a tag from a later commit (<c>--as</c>), the tag; otherwise <c>null</c>.</summary>
    internal string? MeasuredAs { get; init; }

    /// <summary>The files under <c>src/</c> that differ from <see cref="MeasuredAs"/>, none of them code.</summary>
    internal IReadOnlyList<string> SourceDifferences { get; init; } = [];

    /// <summary>Whether the working tree differs from the commit in anything but the baselines directory.</summary>
    internal static bool WorkingTreeChanged() =>
        !string.IsNullOrWhiteSpace(Shell.Git($"status --porcelain -- \":(top,exclude){BaselinesPathspec}\""));

    /// <summary>
    /// Decides the destination. A run is a baseline only when the commit carries a tag of its own (or is filed under
    /// one with <c>--as</c>), the job is the publication job, and no filter narrowed the suites; otherwise it is a
    /// measurement, named after <c>git describe</c> and the moment it started so that no two runs collide.
    /// </summary>
    /// <remarks>
    /// Track B is measured on a tag whose Track A baseline already exists, and is kept beside it in
    /// <c>Baselines/&lt;tag&gt;/market/</c> without touching a file of it. Until that baseline exists a Track B run on
    /// the tag is a measurement.
    /// </remarks>
    internal static RunTarget Decide(bool shortened, string? filter, bool allowDirty, bool market, string? asTag)
    {
        var tag = Shell.Git("describe --tags --exact-match HEAD");
        var describe = Shell.Git("describe --tags --always --long");
        var dirty = WorkingTreeChanged();
        IReadOnlyList<string> differences = [];
        string? measuredAs = null;

        if (string.IsNullOrEmpty(describe))
        {
            describe = "no-git";
        }

        // Every reason, not the first one found: a shortened run on an untagged commit is unusable because of the
        // job, and a reader told only about the missing tag would trust the numbers.
        var blocked = new List<string>();
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var working = Path.Combine(Paths.Measurements, $"{describe}{(market ? "-market" : string.Empty)}-{stamp}");

        if (asTag is not null && asTag != tag)
        {
            if (!SourceIdentity.TagExists(asTag))
            {
                blocked.Add($"--as names {asTag}, which is not a tag of this repository");
            }
            else
            {
                var comparison = SourceIdentity.Compare(asTag);

                if (comparison.SameCode)
                {
                    tag = asTag;
                    measuredAs = asTag;
                    differences = comparison.Differing;
                }
                else
                {
                    blocked.Add($"src/ differs from {asTag} in what may change code: {string.Join(", ", comparison.Code)}");
                }
            }
        }

        if (string.IsNullOrEmpty(tag))
        {
            blocked.Add("HEAD carries no tag");
        }

        if (shortened)
        {
            blocked.Add("the job is shortened, so these are not publication numbers");
        }

        if (filter is not null)
        {
            blocked.Add($"a filter narrowed the run to '{filter}'");
        }

        if (market && !string.IsNullOrEmpty(tag) && !System.IO.Directory.Exists(Path.Combine(Paths.Baselines, tag)))
        {
            blocked.Add($"Track A's baseline of {tag} does not exist yet, and Track B is measured beside it");
        }

        if (blocked.Count == 0)
        {
            return new RunTarget(
                RunKind.Baseline,
                market ? Path.Combine(Paths.Baselines, tag, "market") : Path.Combine(Paths.Baselines, tag),
                working,
                market ? $"{tag}/market" : tag,
                tag,
                describe,
                measuredAs is null
                    ? "every suite of the track, on a tagged commit, under the publication job"
                    : $"every suite of the track, under the publication job, on a commit whose src/ is {measuredAs}'s in code",
                dirty,
                allowDirty)
            {
                MeasuredAs = measuredAs,
                SourceDifferences = differences,
            };
        }

        return new RunTarget(
            RunKind.Measurement,
            working,
            working,
            Path.GetFileName(working),
            string.IsNullOrEmpty(tag) ? null : tag,
            describe,
            string.Join("; ", blocked),
            dirty,
            allowDirty)
        {
            MeasuredAs = measuredAs,
            SourceDifferences = differences,
        };
    }

    /// <summary>
    /// Creates the working directory. A baseline that already exists is never touched, and a baseline is not measured
    /// over uncommitted changes unless <c>--allow-dirty</c> says so deliberately.
    /// </summary>
    internal void Create()
    {
        if (IsBaseline && System.IO.Directory.Exists(Directory))
        {
            throw new InvalidOperationException(
                $"A baseline already exists at {Directory}. It is frozen: delete it deliberately, or re-measure under a " +
                "new tag. Nothing was written.");
        }

        if (IsBaseline && Dirty && !AllowDirty)
        {
            throw new InvalidOperationException(
                $"The working tree has uncommitted changes outside Baselines/, so a baseline named {Tag} would not " +
                "measure what that tag points at. Commit or stash them, or pass --allow-dirty deliberately, which " +
                "records the flag in the run. Nothing was written.");
        }

        System.IO.Directory.CreateDirectory(WorkingDirectory);
    }

    /// <summary>
    /// What this run is once it stopped before its last suite: a measurement, in the directory it was written to,
    /// whatever it was meant to be.
    /// </summary>
    internal RunTarget Interrupted() => this with
    {
        Kind = RunKind.Measurement,
        Directory = WorkingDirectory,
        Label = Path.GetFileName(WorkingDirectory),
        Reason = IsBaseline
            ? "the run was interrupted before every suite of the track ran"
            : $"{Reason}; the run was interrupted",
    };

    /// <summary>Moves a complete baseline to where it is kept, and returns the target that writes there.</summary>
    internal RunTarget Settle()
    {
        if (!IsBaseline || WorkingDirectory == Directory)
        {
            return this;
        }

        if (System.IO.Directory.Exists(Directory))
        {
            throw new InvalidOperationException(
                $"A baseline appeared at {Directory} while this run was measuring. It is frozen and was left as it " +
                $"is; this run stays at {WorkingDirectory}.");
        }

        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Directory)!);
        System.IO.Directory.Move(WorkingDirectory, Directory);
        return this with { WorkingDirectory = Directory };
    }

    internal string File(string name) => Path.Combine(WorkingDirectory, name);
}
