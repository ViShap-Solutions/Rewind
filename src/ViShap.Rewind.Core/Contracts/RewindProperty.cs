using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ViShap.Rewind.Contracts;

/// <summary>
/// The member that code generated for a <see cref="RewindableAttribute"/> class calls to write a tracked property.
/// It is not meant for hand-written code; use <see cref="RewindCell{T}"/> instead.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RewindProperty
{
    /// <summary>
    /// Writes <paramref name="value"/> to <paramref name="field"/>. When a scope is open and has not yet recorded this
    /// property, records the current value first, so that a rollback of the scope restores it through
    /// <paramref name="restore"/>.
    /// </summary>
    /// <typeparam name="TOwner">The type that declares the property.</typeparam>
    /// <typeparam name="T">The type of the property.</typeparam>
    /// <param name="owner">The instance whose property is written.</param>
    /// <param name="field">The storage of the property.</param>
    /// <param name="epoch">The storage in which the property remembers which scope last recorded it.</param>
    /// <param name="value">The new value.</param>
    /// <param name="restore">Writes a value back to the storage of the property of an instance, without recording it.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Write<TOwner, T>(TOwner owner, ref T field, ref long epoch, T value, Action<TOwner, T> restore)
        where TOwner : class
    {
        var scope = ScopeContext.Current;
        if (scope is not null && epoch != scope.Epoch)
        {
            Record(scope, owner, field, restore);
            epoch = scope.Epoch;
        }

        field = value;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Record<TOwner, T>(RewindScope scope, TOwner owner, T previous, Action<TOwner, T> restore)
        where TOwner : class
    {
        scope.Journal.Add(new PropertyEntry<TOwner, T>(owner, previous, restore));
    }
}
