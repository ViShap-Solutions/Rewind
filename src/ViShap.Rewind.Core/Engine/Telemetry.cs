using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace ViShap.Rewind.Engine;

/// <summary>How a scope ended, as metrics and traces report it.</summary>
internal enum Outcome
{
    Committed,
    RolledBack,
    InDoubt,
    Pending,
}

/// <summary>
/// The metrics and traces of <see cref="RewindDiagnostics"/>. Every report is an inlined check of whether anyone
/// listens, and only then a call that measures or traces, so with no listener a scope pays a few field reads and
/// allocates nothing.
/// </summary>
internal static class Telemetry
{
    private const string ActivityName = "rewind.scope";

    private static readonly string? s_version = typeof(Telemetry).Assembly.GetName().Version?.ToString();

    private static readonly Meter s_meter = new(RewindDiagnostics.MeterName, s_version);

    private static readonly Counter<long> s_opened = s_meter.CreateCounter<long>(
        "rewind.scope.opened", "{scope}", "Scopes opened.");

    private static readonly Counter<long> s_closed = s_meter.CreateCounter<long>(
        "rewind.scope.closed", "{scope}", "Scopes whose outcome is final: committed by the outermost scope or its transaction, rolled back, or in doubt.");

    private static readonly Histogram<long> s_entries = s_meter.CreateHistogram<long>(
        "rewind.scope.entries", "{entry}", "Journal entries an outermost scope and the scopes nested in it recorded, at its outcome.");

    private static readonly Counter<long> s_compensationFailures = s_meter.CreateCounter<long>(
        "rewind.compensation.failures", "{failure}", "Compensations and rollback effects that threw.");

    private static readonly Counter<long> s_effectFailures = s_meter.CreateCounter<long>(
        "rewind.effect.failures", "{failure}", "Change notifications, OnCommitted observers and AfterCommit effects that threw.");

    private static readonly ActivitySource s_source = new(RewindDiagnostics.ActivitySourceName, s_version);

    private static readonly ConditionalWeakTable<RewindScope, Activity> s_activities = new();

    private static readonly KeyValuePair<string, object?> s_sync = new("rewind.scope.kind", "sync");
    private static readonly KeyValuePair<string, object?> s_async = new("rewind.scope.kind", "async");
    private static readonly KeyValuePair<string, object?> s_nested = new("rewind.scope.nested", true);
    private static readonly KeyValuePair<string, object?> s_outermost = new("rewind.scope.nested", false);
    private static readonly KeyValuePair<string, object?> s_committed = new("rewind.scope.outcome", "committed");
    private static readonly KeyValuePair<string, object?> s_rolledBack = new("rewind.scope.outcome", "rolled_back");
    private static readonly KeyValuePair<string, object?> s_inDoubt = new("rewind.scope.outcome", "in_doubt");
    private static readonly KeyValuePair<string, object?> s_pending = new("rewind.scope.outcome", "pending");

    /// <summary>Reports a scope just opened; an outermost one starts an activity when a listener samples it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Opened(RewindScope scope)
    {
        if (s_opened.Enabled || s_source.HasListeners())
        {
            OpenedObserved(scope);
        }
    }

    /// <summary>Reports a nested scope that committed into its parent.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void NestedCommitted(RewindScope scope)
    {
        if (s_closed.Enabled)
        {
            s_closed.Add(1, Kind(scope.IsAsync), s_nested, s_committed);
        }
    }

    /// <summary>Reports a nested scope that rolled back, and adds an event to the activity of its outermost scope.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void NestedRolledBack(RewindScope scope, int failures)
    {
        if (s_closed.Enabled || failures > 0 || s_source.HasListeners())
        {
            NestedRolledBackObserved(scope, failures);
        }
    }

    /// <summary>
    /// Reports an outermost scope closed with its final <paramref name="outcome"/> — its journal of
    /// <paramref name="entries"/> and the failures of its completion — and ends its activity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Closed(RewindScope scope, Outcome outcome, int entries, int failures)
    {
        if (s_closed.Enabled || s_entries.Enabled || failures > 0 || s_source.HasListeners())
        {
            ClosedObserved(scope, outcome, entries, failures);
        }
    }

    /// <summary>Ends the activity of an outermost scope that closed pending in a transaction.</summary>
    public static void Pending(RewindScope scope, int entries)
    {
        if (s_source.HasListeners())
        {
            StopObserved(scope, Outcome.Pending, entries, 0);
        }
    }

    /// <summary>Reports the final outcome of an outermost scope that waited for its transaction.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void TransactionClosed(bool isAsync, Outcome outcome, int entries, int failures) =>
        OutermostClosedObserved(isAsync, outcome, entries, failures);

    /// <summary>Marks the activity of an outermost scope failed by <paramref name="exception"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Failed(RewindScope scope, Exception exception)
    {
        if (s_source.HasListeners())
        {
            FailedObserved(scope, exception);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ClosedObserved(RewindScope scope, Outcome outcome, int entries, int failures)
    {
        OutermostClosedObserved(scope.IsAsync, outcome, entries, failures);
        StopObserved(scope, outcome, entries, failures);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OpenedObserved(RewindScope scope)
    {
        var nested = scope.Parent is not null;
        if (s_opened.Enabled)
        {
            s_opened.Add(1, Kind(scope.IsAsync), nested ? s_nested : s_outermost);
        }

        if (!nested && s_source.HasListeners() && s_source.StartActivity(ActivityName, ActivityKind.Internal) is { } activity)
        {
            activity.SetTag(Kind(scope.IsAsync).Key, Kind(scope.IsAsync).Value);
            s_activities.AddOrUpdate(scope, activity);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void NestedRolledBackObserved(RewindScope scope, int failures)
    {
        if (s_closed.Enabled)
        {
            s_closed.Add(1, Kind(scope.IsAsync), s_nested, s_rolledBack);
        }

        CompensationsFailed(failures);
        if (s_activities.TryGetValue(Root(scope), out var activity))
        {
            activity.AddEvent(new ActivityEvent("rewind.scope.rolled_back", tags: new ActivityTagsCollection { ["rewind.scope.depth"] = scope.Depth }));
        }
    }

    private static void OutermostClosedObserved(bool isAsync, Outcome outcome, int entries, int failures)
    {
        if (s_closed.Enabled)
        {
            s_closed.Add(1, Kind(isAsync), s_outermost, Tag(outcome));
        }

        if (s_entries.Enabled)
        {
            s_entries.Record(entries, Kind(isAsync), Tag(outcome));
        }

        if (outcome == Outcome.RolledBack)
        {
            CompensationsFailed(failures);
        }
        else
        {
            EffectsFailed(failures);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FailedObserved(RewindScope scope, Exception exception)
    {
        if (scope.Parent is null && s_activities.TryGetValue(scope, out var activity))
        {
#if NET9_0_OR_GREATER
            activity.AddException(exception);
#else
            // The event Activity.AddException records on later runtimes, under the OpenTelemetry exception conventions.
            activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.message", exception.Message },
                { "exception.stacktrace", exception.ToString() },
                { "exception.type", exception.GetType().FullName },
            }));
#endif
            activity.SetStatus(ActivityStatusCode.Error, exception.GetType().FullName);
        }
    }

    private static void StopObserved(RewindScope scope, Outcome outcome, int entries, int failures)
    {
        if (!s_activities.TryGetValue(scope, out var activity))
        {
            return;
        }

        s_activities.Remove(scope);
        var tag = Tag(outcome);
        activity.SetTag(tag.Key, tag.Value);
        activity.SetTag("rewind.scope.entries", entries);
        activity.SetTag("rewind.scope.failures", failures);
        activity.Stop();
    }

    private static void CompensationsFailed(int failures)
    {
        if (failures > 0 && s_compensationFailures.Enabled)
        {
            s_compensationFailures.Add(failures);
        }
    }

    private static void EffectsFailed(int failures)
    {
        if (failures > 0 && s_effectFailures.Enabled)
        {
            s_effectFailures.Add(failures);
        }
    }

    private static KeyValuePair<string, object?> Kind(bool isAsync) => isAsync ? s_async : s_sync;

    private static KeyValuePair<string, object?> Tag(Outcome outcome) => outcome switch
    {
        Outcome.Committed => s_committed,
        Outcome.RolledBack => s_rolledBack,
        Outcome.InDoubt => s_inDoubt,
        _ => s_pending,
    };

    private static RewindScope Root(RewindScope scope)
    {
        while (scope.Parent is { } parent)
        {
            scope = parent;
        }

        return scope;
    }
}
