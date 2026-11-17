using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ViShap.Rewind.Tests.Observability;

/// <summary>
/// The collection of the tests that subscribe listeners to the whole process — the observability tests and the
/// documentation examples — run alone, so the listeners neither see nor disturb other tests' scopes.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetryCollection
{
    /// <summary>The name of the collection.</summary>
    public const string Name = "Telemetry";
}

/// <summary>One measurement of a Rewind instrument: its name, value and tags.</summary>
public sealed record Measurement(string Instrument, long Value, IReadOnlyDictionary<string, object?> Tags)
{
    /// <summary>Returns the value of tag <paramref name="key"/>, or <see langword="null"/>.</summary>
    public object? Tag(string key) => Tags.TryGetValue(key, out var value) ? value : null;
}

/// <summary>
/// Listens to the meter and the activity source of <see cref="RewindDiagnostics"/> from its creation to its disposal,
/// and keeps every measurement and every stopped activity.
/// </summary>
public sealed class TelemetryRecorder : IDisposable
{
    private readonly MeterListener _meters = new();
    private readonly ActivityListener _activities;
    private readonly List<Measurement> _measurements = [];
    private readonly List<Activity> _stopped = [];

    /// <summary>Starts listening.</summary>
    public TelemetryRecorder()
    {
        _meters.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == RewindDiagnostics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
                lock (Instruments)
                {
                    Instruments.Add(instrument);
                }
            }
        };
        _meters.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var copy = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            lock (_measurements)
            {
                _measurements.Add(new Measurement(instrument.Name, value, copy));
            }
        });
        _meters.Start();

        _activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name is RewindDiagnostics.ActivitySourceName or UserSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_stopped)
                {
                    _stopped.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_activities);
    }

    private const string UserSourceName = "ViShap.Rewind.Tests.User";

    /// <summary>Gets a source standing for the application's own tracing.</summary>
    public static ActivitySource UserSource { get; } = new(UserSourceName);

    /// <summary>Gets the Rewind instruments the listener was told about.</summary>
    public List<Instrument> Instruments { get; } = [];

    /// <summary>Returns the measurements of <paramref name="instrument"/> taken so far.</summary>
    public List<Measurement> Of(string instrument)
    {
        lock (_measurements)
        {
            return _measurements.Where(measurement => measurement.Instrument == instrument).ToList();
        }
    }

    /// <summary>Returns the activities of <paramref name="source"/> stopped so far, in the order they stopped.</summary>
    public List<Activity> Stopped(string source)
    {
        lock (_stopped)
        {
            return _stopped.Where(activity => activity.Source.Name == source).ToList();
        }
    }

    /// <summary>Stops listening.</summary>
    public void Dispose()
    {
        _activities.Dispose();
        _meters.Dispose();
    }
}
