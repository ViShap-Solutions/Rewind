using System.Runtime.CompilerServices;

namespace ViShap.Rewind.Engine;

/// <summary>Compares an owner and a member by reference, whatever equality their types define.</summary>
internal sealed class IdentityPairComparer : IEqualityComparer<(object Owner, object? Member)>
{
    public static readonly IdentityPairComparer Instance = new();

    public bool Equals((object Owner, object? Member) x, (object Owner, object? Member) y) =>
        ReferenceEquals(x.Owner, y.Owner) && ReferenceEquals(x.Member, y.Member);

    public int GetHashCode((object Owner, object? Member) pair) =>
        HashCode.Combine(RuntimeHelpers.GetHashCode(pair.Owner), pair.Member is null ? 0 : RuntimeHelpers.GetHashCode(pair.Member));
}
