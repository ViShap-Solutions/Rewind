namespace ViShap.Rewind;

/// <summary>
/// The names under which the library reports what its scopes do: a <see cref="System.Diagnostics.Metrics.Meter"/> with
/// scopes opened and closed, journal entries per scope and failures of compensations and effects, and an
/// <see cref="System.Diagnostics.ActivitySource"/> with an activity per outermost scope. Nothing is measured or traced
/// unless a listener subscribes to these names.
/// </summary>
public static class RewindDiagnostics
{
    /// <summary>The name of the meter: subscribe to it — for instance with OpenTelemetry's <c>AddMeter</c> — to receive the metrics.</summary>
    public const string MeterName = "ViShap.Rewind";

    /// <summary>The name of the activity source: subscribe to it — for instance with OpenTelemetry's <c>AddSource</c> — to receive the traces.</summary>
    public const string ActivitySourceName = "ViShap.Rewind";
}
