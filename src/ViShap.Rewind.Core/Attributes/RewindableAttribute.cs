namespace ViShap.Rewind;

/// <summary>
/// Marks a <see langword="partial"/> class whose <see langword="partial"/> properties the source generator implements
/// as tracked state: a write made while a scope is open is undone when that scope rolls back.
/// </summary>
/// <remarks>
/// Declare each property to track as <c>public partial T Name { get; set; }</c>; the generator writes its
/// implementation. A property that is not <see langword="partial"/> is not tracked. Mark a partial property with
/// <see cref="RewindIgnoreAttribute"/> to keep it as plain storage.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RewindableAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public RewindableAttribute()
    {
    }
}
