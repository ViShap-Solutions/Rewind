using System.Diagnostics.Metrics;
using System.Transactions;
using ViShap.Rewind.Engine;
using ViShap.Rewind.Tests.Fixtures;

namespace ViShap.Rewind.Tests.Observability;

[Collection(TelemetryCollection.Name)]
public sealed class MetricsTests
{
    [Fact]
    public async Task Scopes_OpenedAndClosed_CountedWithKindNestingAndOutcome()
    {
        var cell = new RewindCell<int>(0);
        using var recorder = new TelemetryRecorder();

        Rewind.Run(() =>
        {
            cell.Value = 1;
            Rewind.Run(() => cell.Value = 2);
            Assert.Throws<InvalidOperationException>(() => Rewind.Run(() => throw new InvalidOperationException()));
        });
        Assert.Throws<InvalidOperationException>(() => Rewind.Run(() => throw new InvalidOperationException()));
        await Rewind.RunAsync(async () =>
        {
            await OtherThread.Resume();
            cell.Value = 3;
        });

        Assert.Equal(
            new[] { "sync False", "sync True", "sync True", "sync False", "async False" },
            recorder.Of("rewind.scope.opened").Select(m => $"{m.Tag("rewind.scope.kind")} {m.Tag("rewind.scope.nested")}"));
        Assert.Equal(
            new[] { "sync True committed", "sync True rolled_back", "sync False committed", "sync False rolled_back", "async False committed" },
            recorder.Of("rewind.scope.closed").Select(m => $"{m.Tag("rewind.scope.kind")} {m.Tag("rewind.scope.nested")} {m.Tag("rewind.scope.outcome")}"));
        Assert.All(recorder.Of("rewind.scope.opened").Concat(recorder.Of("rewind.scope.closed")), m => Assert.Equal(1, m.Value));
    }

    [Fact]
    public void Entries_RecordedAtOutermostOutcome()
    {
        var first = new RewindCell<int>(0);
        var second = new RewindCell<int>(0);
        var list = new RewindList<int>();
        using var recorder = new TelemetryRecorder();

        Rewind.Run(() =>
        {
            first.Value = 1;
            Rewind.Run(() => second.Value = 1);
            list.Add(1);
        });
        using (Rewind.Begin())
        {
            first.Value = 2;
            second.Value = 2;
        }

        Assert.Equal(
            new[] { "3 committed", "2 rolled_back" },
            recorder.Of("rewind.scope.entries").Select(m => $"{m.Value} {m.Tag("rewind.scope.outcome")}"));
    }

    [Fact]
    public void Failures_CountedByKind()
    {
        using var recorder = new TelemetryRecorder();

        Assert.Throws<RewindCompensationException>(() => Rewind.Run(scope =>
        {
            scope.Effect(() => { }, () => throw new InvalidOperationException());
            scope.AfterRollback(() => throw new InvalidOperationException());
            throw new ArgumentException();
        }));
        Assert.Throws<RewindEffectException>(() => Rewind.Run(scope => scope.AfterCommit(() => throw new InvalidOperationException())));

        Assert.Equal(2, recorder.Of("rewind.compensation.failures").Sum(m => m.Value));
        Assert.Equal(1, recorder.Of("rewind.effect.failures").Sum(m => m.Value));
    }

    [Fact]
    public void TransactionScope_ClosedAtTransactionOutcome()
    {
        var cell = new RewindCell<int>(0);
        using var recorder = new TelemetryRecorder();

        using (var transaction = new TransactionScope())
        {
            Rewind.Run(() => cell.Value = 1);
            Assert.Empty(recorder.Of("rewind.scope.closed"));
            transaction.Complete();
        }

        using (new TransactionScope())
        {
            Rewind.Run(() => cell.Value = 2);
        }

        using (new TransactionScope())
        {
            Rewind.Run(() => cell.Value = 3);
            TransactionParticipant.Of(Transaction.Current!)!.InDoubt();
        }

        Assert.Equal(
            new[] { "committed", "rolled_back", "in_doubt" },
            recorder.Of("rewind.scope.closed").Select(m => (string)m.Tag("rewind.scope.outcome")!));
        Assert.Equal(
            new[] { "pending", "pending", "pending" },
            recorder.Stopped(RewindDiagnostics.ActivitySourceName).Select(activity => (string)activity.GetTagItem("rewind.scope.outcome")!));
    }

    [Fact]
    public void Meter_HasDocumentedNameAndInstruments()
    {
        using var recorder = new TelemetryRecorder();
        Rewind.Run(() => { });

        Assert.Equal(
            new[]
            {
                "rewind.compensation.failures Counter`1 {failure}",
                "rewind.effect.failures Counter`1 {failure}",
                "rewind.scope.closed Counter`1 {scope}",
                "rewind.scope.entries Histogram`1 {entry}",
                "rewind.scope.opened Counter`1 {scope}",
            },
            recorder.Instruments.Select(instrument => $"{instrument.Name} {instrument.GetType().Name} {instrument.Unit}").Order(StringComparer.Ordinal));
        Assert.All(recorder.Instruments, instrument => Assert.Equal(RewindDiagnostics.MeterName, instrument.Meter.Name));
        Assert.All(recorder.Instruments, instrument => Assert.False(string.IsNullOrEmpty(instrument.Description)));
        Assert.Equal("ViShap.Rewind", RewindDiagnostics.MeterName);
        Assert.Equal("ViShap.Rewind", RewindDiagnostics.ActivitySourceName);
        Assert.IsAssignableFrom<Instrument>(recorder.Instruments[0]);
    }
}
