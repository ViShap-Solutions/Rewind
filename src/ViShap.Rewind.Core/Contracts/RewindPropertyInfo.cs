using System.ComponentModel;

namespace ViShap.Rewind.Contracts;

/// <summary>
/// Describes a tracked property of a <see cref="RewindableAttribute"/> class to the engine: its name, how to read and
/// write its storage without recording, and how to raise its change notification. Created once per property by
/// generated code; not meant for hand-written code.
/// </summary>
/// <typeparam name="TOwner">The type that declares the property.</typeparam>
/// <typeparam name="T">The type of the property.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RewindPropertyInfo<TOwner, T>
    where TOwner : class
{
    /// <summary>Describes a property that raises no change notification.</summary>
    /// <param name="name">The name of the property.</param>
    /// <param name="get">Reads the storage of the property of an instance.</param>
    /// <param name="set">Writes the storage of the property of an instance, without recording it.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public RewindPropertyInfo(string name, Func<TOwner, T> get, Action<TOwner, T> set)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        Name = name;
        Get = get;
        Set = set;
    }

    /// <summary>Describes a property that raises a change notification.</summary>
    /// <param name="name">The name of the property.</param>
    /// <param name="get">Reads the storage of the property of an instance.</param>
    /// <param name="set">Writes the storage of the property of an instance, without recording it.</param>
    /// <param name="notify">Raises the change notification of the property of an instance.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public RewindPropertyInfo(string name, Func<TOwner, T> get, Action<TOwner, T> set, Action<TOwner> notify)
        : this(name, get, set)
    {
        ArgumentNullException.ThrowIfNull(notify);
        Notify = notify;
    }

    /// <summary>Gets the name of the property.</summary>
    public string Name { get; }

    internal Func<TOwner, T> Get { get; }

    internal Action<TOwner, T> Set { get; }

    internal Action<TOwner>? Notify { get; }
}
