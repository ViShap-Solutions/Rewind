using System.ComponentModel;

namespace ViShap.Rewind.Tests.Fixtures;

/// <summary>A base that raises <see cref="PropertyChanged"/> through <see cref="OnPropertyChanged"/>, as MVVM base classes do.</summary>
public abstract class Observable : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises <see cref="PropertyChanged"/> for <paramref name="name"/>.</summary>
    protected void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A rewindable document whose notifications go through its base's <c>OnPropertyChanged(string)</c>.</summary>
[Rewindable]
public sealed partial class Document : Observable
{
    /// <summary>Gets or sets the title.</summary>
    public partial string Title { get; set; }

    /// <summary>Gets or sets the page count.</summary>
    public partial int Pages { get; set; }

    /// <summary>Gets the lines.</summary>
    public RewindList<string> Lines { get; } = [];

    /// <summary>Gets the tags and their weights.</summary>
    public RewindDictionary<string, int> Tags { get; } = [];

    /// <summary>Gets the readers.</summary>
    public RewindSet<string> Readers { get; } = [];
}

/// <summary>A rewindable counter whose notifications go through its own field-like event.</summary>
[Rewindable]
public sealed partial class Counter : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets or sets the count.</summary>
    public partial int Count { get; set; }
}

/// <summary>Records the property names an object raises, with the scope and the value seen at that moment.</summary>
public sealed class NotificationLog
{
    private readonly List<string> _names = [];

    /// <summary>Starts recording what <paramref name="source"/> raises; <paramref name="read"/> reads a value to record with each name.</summary>
    public NotificationLog(INotifyPropertyChanged source, Func<string?, object?>? read = null)
    {
        source.PropertyChanged += (_, e) =>
        {
            _names.Add(e.PropertyName!);
            Scopes.Add(Rewind.Current);
            Values.Add(read?.Invoke(e.PropertyName));
        };
    }

    /// <summary>Gets the names raised, in order.</summary>
    public IReadOnlyList<string> Names => _names;

    /// <summary>Gets the current scope at each notification.</summary>
    public List<RewindScope?> Scopes { get; } = [];

    /// <summary>Gets the value read at each notification.</summary>
    public List<object?> Values { get; } = [];
}
