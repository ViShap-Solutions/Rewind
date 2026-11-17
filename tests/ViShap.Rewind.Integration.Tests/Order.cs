using System.ComponentModel;

namespace ViShap.Rewind.Integration.Tests;

/// <summary>An in-memory aggregate kept beside the database: what must end as the database ends.</summary>
[Rewindable]
public partial class Order : INotifyPropertyChanged
{
    public partial string Status { get; set; }

    public RewindList<string> Lines { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
}
