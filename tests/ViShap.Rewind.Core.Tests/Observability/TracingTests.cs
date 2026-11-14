using System.Diagnostics;
using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Observability;

[Collection(TelemetryCollection.Name)]
public sealed class TracingTests
{
    [Fact]
    public void OutermostScope_StartsActivityThatParentsTheBlock()
    {
        var cell = new RewindCell<int>(0);
        using var recorder = new TelemetryRecorder();
        Activity? inBlock = null;
        Activity? inEffect = null;
        Activity? user = null;

        Rewind.Run(scope =>
        {
            inBlock = Activity.Current;
            cell.Value = 1;
            Rewind.Run(() => cell.Value = 2);
            using (user = TelemetryRecorder.UserSource.StartActivity("user work"))
            {
            }

            scope.AfterCommit(() => inEffect = Activity.Current);
        });

        var rewind = Assert.Single(recorder.Stopped(RewindDiagnostics.ActivitySourceName));
        Assert.Equal("rewind.scope", rewind.OperationName);
        Assert.Same(rewind, inBlock);
        Assert.Same(rewind, inEffect);
        Assert.Same(rewind, user!.Parent);
        Assert.Equal("sync", rewind.GetTagItem("rewind.scope.kind"));
        Assert.Equal("committed", rewind.GetTagItem("rewind.scope.outcome"));
        Assert.Equal(2, rewind.GetTagItem("rewind.scope.entries"));
        Assert.Equal(0, rewind.GetTagItem("rewind.scope.failures"));
        Assert.Equal(ActivityStatusCode.Unset, rewind.Status);
        Assert.Null(Activity.Current);
    }

    [Fact]
    public void NestedRollback_IsEventOnOutermostActivity()
    {
        var cell = new RewindCell<int>(0);
        using var recorder = new TelemetryRecorder();

        Rewind.Run(() =>
        {
            Rewind.Run(() => Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
            {
                cell.Value = 1;
                throw new InvalidOperationException();
            })));
        });

        var rewind = Assert.Single(recorder.Stopped(RewindDiagnostics.ActivitySourceName));
        var rolledBack = Assert.Single(rewind.Events);
        Assert.Equal("rewind.scope.rolled_back", rolledBack.Name);
        Assert.Equal(2, rolledBack.Tags.Single(tag => tag.Key == "rewind.scope.depth").Value);
        Assert.Equal("committed", rewind.GetTagItem("rewind.scope.outcome"));
    }

    [Fact]
    public void FailedBlock_SetsErrorStatusAndException()
    {
        var cell = new RewindCell<int>(0);
        using var recorder = new TelemetryRecorder();

        Assert.Throws<InvalidOperationException>(() => Rewind.Run(() =>
        {
            cell.Value = 1;
            throw new InvalidOperationException("declined");
        }));
        using (Rewind.Begin())
        {
            cell.Value = 2;
        }

        var activities = recorder.Stopped(RewindDiagnostics.ActivitySourceName);
        Assert.Equal(2, activities.Count);
        Assert.Equal(ActivityStatusCode.Error, activities[0].Status);
        Assert.Equal("rolled_back", activities[0].GetTagItem("rewind.scope.outcome"));
        Assert.Contains(activities[0].Events, e => e.Name == "exception"
            && e.Tags.Any(tag => tag.Key == "exception.type" && Equals(tag.Value, typeof(InvalidOperationException).FullName)));
        Assert.Equal(ActivityStatusCode.Unset, activities[1].Status);
        Assert.Equal("rolled_back", activities[1].GetTagItem("rewind.scope.outcome"));
    }

    [Fact]
    public async Task AsyncScope_ActivitySpansAwaits()
    {
        var cell = new RewindCell<int>(0);
        using var recorder = new TelemetryRecorder();
        var seen = new List<Activity?>();

        await Rewind.RunAsync(async () =>
        {
            seen.Add(Activity.Current);
            await OtherThread.Resume();
            cell.Value = 1;
            seen.Add(Activity.Current);
        });

        var rewind = Assert.Single(recorder.Stopped(RewindDiagnostics.ActivitySourceName));
        Assert.All(seen, activity => Assert.Same(rewind, activity));
        Assert.Equal("async", rewind.GetTagItem("rewind.scope.kind"));
        Assert.Equal("committed", rewind.GetTagItem("rewind.scope.outcome"));
    }

    [Fact]
    public void NoListener_NothingMeasuredOrTraced()
    {
        var cell = new RewindCell<int>(0);
        Activity? inBlock = new("sentinel");

        Rewind.Run(() =>
        {
            cell.Value = 1;
            inBlock = Activity.Current;
        });

        Assert.Null(inBlock);
        using var meters = new System.Diagnostics.Metrics.MeterListener();
        var instruments = new List<System.Diagnostics.Metrics.Instrument>();
        meters.InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name == RewindDiagnostics.MeterName)
            {
                instruments.Add(instrument);
            }
        };
        meters.Start();
        Assert.Equal(5, instruments.Count);
#if NET9_0_OR_GREATER
        // .NET 8 keeps an instrument enabled after the listener that enabled it is disposed, so with the other
        // telemetry tests' listeners gone the flag says nothing there; the measurements still reach no one.
        Assert.All(instruments, instrument => Assert.False(instrument.Enabled));
#endif
    }
}
