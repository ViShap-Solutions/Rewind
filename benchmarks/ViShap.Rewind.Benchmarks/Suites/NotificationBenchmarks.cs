using System.Collections.ObjectModel;
using System.ComponentModel;
using BenchmarkDotNet.Attributes;

namespace ViShap.Rewind.Benchmarks.Suites;

/// <summary>
/// W15 — what notifying costs when something subscribes: a list changed with no scope against an
/// <see cref="ObservableCollection{T}"/> doing the same, a list changed in a committed scope (one reset), and a notifying
/// property committed and rolled back (one notification each). Each invocation leaves the state as it found it.
/// </summary>
public class NotificationBenchmarks
{
    private readonly ObservableCollection<int> _observable = [];
    private readonly RewindList<int> _list = [];
    private readonly NotifyingNote _note = new() { Title = 0 };
    private int _seen;

    [GlobalSetup]
    public void Setup()
    {
        _observable.CollectionChanged += (_, _) => _seen++;
        _list.CollectionChanged += (_, _) => _seen++;
        _note.PropertyChanged += (_, _) => _seen++;
    }

    [Benchmark(Baseline = true)]
    public int PlainObservableCollectionAddRemove()
    {
        _observable.Add(1);
        _observable.RemoveAt(0);
        return _seen;
    }

    [Benchmark]
    public int ListAddRemoveNoScope()
    {
        _list.Add(1);
        _list.RemoveAt(0);
        return _seen;
    }

    [Benchmark]
    public int ListAddRemoveCommitted()
    {
        using var scope = Rewind.Begin();
        _list.Add(1);
        _list.RemoveAt(0);
        scope.Commit();
        return _seen;
    }

    [Benchmark]
    public int PropertyCommitted()
    {
        using var scope = Rewind.Begin();
        _note.Title++;
        scope.Commit();
        return _seen;
    }

    [Benchmark]
    public int PropertyRolledBack()
    {
        using (Rewind.Begin())
        {
            _note.Title++;
        }

        return _seen;
    }
}

/// <summary>A rewindable type with one tracked property that notifies.</summary>
[Rewindable]
public sealed partial class NotifyingNote : INotifyPropertyChanged
{
    /// <summary>Occurs when a tracked property changed.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets or sets the title.</summary>
    public partial int Title { get; set; }
}
