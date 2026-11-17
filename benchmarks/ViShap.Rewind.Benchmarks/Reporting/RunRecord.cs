using System.Text.Json;
using System.Text.Json.Serialization;
using ViShap.Rewind.Benchmarks.Environment;

namespace ViShap.Rewind.Benchmarks.Reporting;

/// <summary>
/// What a run was, written beside its results as <c>run.json</c>. The report is a view over the raw files and over
/// nothing else, so the identity of the run has to be one of them: a report regenerated from a committed baseline a
/// year later says the same thing as the one generated the day it was taken.
/// </summary>
/// <remarks>
/// A run that ended before its last suite says so: <c>Interrupted</c> is set, <c>StoppedBy</c> names how it ended,
/// <c>SuitesUnfinished</c> names the suite that was cut off, and <c>SuitesNotRun</c> names every suite and step after
/// it. While a run is in progress the record on disk already reads as interrupted, so a process that dies leaves a
/// true record behind it.
/// </remarks>
internal sealed record RunRecord(
    string Kind,
    string Label,
    string? Tag,
    string Describe,
    string Revision,
    bool Dirty,
    bool AllowDirty,
    string Reason,
    string Track,
    string Job,
    string? Filter,
    string StartedUtc,
    string FinishedUtc,
    IReadOnlyList<string> Commands,
    IReadOnlyList<string> SuitesRun,
    IReadOnlyList<string> SuitesNotRun,
    bool Interrupted = false,
    string? StoppedBy = null,
    IReadOnlyList<string>? SuitesUnfinished = null,
    string? MeasuredAs = null,
    IReadOnlyList<string>? SourceDifferences = null)
{
    /// <summary>How the record of a Track A run names its track.</summary>
    internal const string TrackA = "A — Rewind alone";

    /// <summary>How the record of a Track B run names its track.</summary>
    internal const string TrackB = "B — Rewind beside the alternatives";

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    internal bool IsBaseline => Kind == nameof(RunKind.Baseline);

    internal bool IsMarket => Track == TrackB;

    internal void Write(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, Format));

    internal static RunRecord Read(string path) =>
        JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(path), Format)
        ?? throw new InvalidOperationException($"{path} does not describe a run.");
}
