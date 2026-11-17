using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ViShap.Rewind.Contracts;

/// <summary>
/// The members that code generated for a <see cref="RewindableAttribute"/> class calls to write a tracked property.
/// They are not meant for hand-written code; use <see cref="RewindCell{T}"/> instead.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RewindProperty
{
    /// <summary>
    /// Writes <paramref name="value"/> to <paramref name="field"/>. When a scope is open and has not yet recorded this
    /// property, records the current value first, so that a rollback of the scope restores it.
    /// </summary>
    /// <typeparam name="TOwner">The type that declares the property.</typeparam>
    /// <typeparam name="T">The type of the property.</typeparam>
    /// <param name="owner">The instance whose property is written.</param>
    /// <param name="field">The storage of the property.</param>
    /// <param name="epoch">The storage in which the property remembers which scope last recorded it.</param>
    /// <param name="value">The new value.</param>
    /// <param name="property">The description of the property.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<TOwner, T>(TOwner owner, ref T field, ref long epoch, T value, RewindPropertyInfo<TOwner, T> property)
        where TOwner : class
    {
        var scope = ScopeContext.Current;
        if (scope is not null && epoch != scope.Epoch)
        {
            Record(scope, owner, field, property);
            epoch = scope.Epoch;
        }

        field = value;
    }

    /// <summary>
    /// Writes <paramref name="value"/> to <paramref name="field"/> of a property that raises a change notification.
    /// With no scope open, raises the notification at once when the value changes; with a scope open, records the
    /// current value as <see cref="Write{TOwner, T}"/> does, and the notification is raised once after the outermost
    /// scope commits, if the value then differs from the one at the start of the scope.
    /// </summary>
    /// <typeparam name="TOwner">The type that declares the property.</typeparam>
    /// <typeparam name="T">The type of the property.</typeparam>
    /// <param name="owner">The instance whose property is written.</param>
    /// <param name="field">The storage of the property.</param>
    /// <param name="epoch">The storage in which the property remembers which scope last recorded it.</param>
    /// <param name="value">The new value.</param>
    /// <param name="property">The description of the property, with its notification.</param>
    public static void WriteAndNotify<TOwner, T>(TOwner owner, ref T field, ref long epoch, T value, RewindPropertyInfo<TOwner, T> property)
        where TOwner : class
    {
        var scope = ScopeContext.Current;
        if (scope is null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            property.Notify?.Invoke(owner);
            return;
        }

        if (epoch != scope.Epoch)
        {
            Record(scope, owner, field, property);
            epoch = scope.Epoch;
        }

        field = value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Record<TOwner, T>(RewindScope scope, TOwner owner, T previous, RewindPropertyInfo<TOwner, T> property)
        where TOwner : class
    {
        scope.Journal.Add(new PropertyEntry<TOwner, T>(owner, previous, property));
    }
}
