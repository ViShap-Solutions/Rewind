namespace ViShap.Rewind;

/// <summary>
/// Excludes a property of a <see cref="RewindableAttribute"/> class from tracking. On a <see langword="partial"/>
/// property the generator implements plain storage that a rollback does not restore; on any other writable property
/// it states that the property is deliberately untracked.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class RewindIgnoreAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    public RewindIgnoreAttribute()
    {
    }
}
