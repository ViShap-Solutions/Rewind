namespace ViShap.Rewind.Tests.Verify;

/// <summary>A rewindable order, the subject of the operations <c>RewindVerify</c> is tried on.</summary>
[Rewindable]
public sealed partial class Order
{
    /// <summary>Gets or sets the status.</summary>
    public partial string Status { get; set; }

    /// <summary>Gets or sets the total.</summary>
    public partial decimal Total { get; set; }
}
