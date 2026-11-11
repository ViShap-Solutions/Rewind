namespace ViShap.Rewind.Tests.Fixtures;

/// <summary>A tracked integer, held either by a <see cref="RewindCell{T}"/> or by a generated rewindable property.</summary>
public interface ISlot
{
    /// <summary>Gets or sets the tracked value.</summary>
    int Value { get; set; }
}

/// <summary>The hand-written shape: a <see cref="RewindCell{T}"/>.</summary>
public sealed class CellSlot(int value) : ISlot
{
    private readonly RewindCell<int> _cell = new(value);

    /// <inheritdoc />
    public int Value
    {
        get => _cell.Value;
        set => _cell.Value = value;
    }
}

/// <summary>The generated shape: a partial property of a rewindable class.</summary>
[Rewindable]
public sealed partial class PropertySlot : ISlot
{
    /// <summary>Creates the slot holding <paramref name="value"/>.</summary>
    public PropertySlot(int value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public partial int Value { get; set; }

    /// <summary>Gets or sets a value that is not tracked.</summary>
    [RewindIgnore]
    public partial int Untracked { get; set; }

    /// <summary>Gets or sets a tracked reference.</summary>
    public partial string Name { get; set; }
}
