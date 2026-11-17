using System.Collections.Specialized;
using System.ComponentModel;

namespace ViShap.Rewind.Engine;

/// <summary>A Rewind collection that raises change notifications.</summary>
internal interface ICollectionNotifier
{
    /// <summary>Gets whether anything subscribes to the collection's notifications.</summary>
    bool Observed { get; }

    /// <summary>Raises what a change of any kind raises: a list a reset and its properties, a dictionary or set its properties.</summary>
    void NotifyReset();
}

/// <summary>The event arguments every Rewind collection raises, shared because they carry no state.</summary>
internal static class CollectionNotifications
{
    public static readonly PropertyChangedEventArgs Count = new("Count");

    public static readonly PropertyChangedEventArgs Indexer = new("Item[]");

    public static readonly PropertyChangedEventArgs Keys = new("Keys");

    public static readonly PropertyChangedEventArgs Values = new("Values");

    public static readonly NotifyCollectionChangedEventArgs Reset = new(NotifyCollectionChangedAction.Reset);
}
