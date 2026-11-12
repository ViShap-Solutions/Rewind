using System.Text;
using ViShap.Rewind.Engine;

namespace ViShap.Rewind.Testing;

/// <summary>
/// Counts the failure points an operation passes, fails it at one of them, notes the points when asked, and observes
/// whether a deferred effect runs.
/// </summary>
/// <param name="failAt">The point to fail at, counted from 1; 0 to fail at none.</param>
/// <param name="logPoints">Whether to keep the description of every point passed.</param>
internal sealed class Injector(int failAt, bool logPoints) : IFaultInjector
{
    private int _points;
    private int _deferredRan;

    /// <summary>Gets how many failure points the operation passed.</summary>
    public int Points => Volatile.Read(ref _points);

    /// <summary>Gets how many deferred effects ran.</summary>
    public int DeferredRan => Volatile.Read(ref _deferredRan);

    /// <summary>Gets the description of the point the operation was failed at, or <see langword="null"/> when it was not reached.</summary>
    public string? FailedPoint { get; private set; }

    /// <summary>Gets the description of every point passed, when they are kept.</summary>
    public List<string> Log { get; } = [];

    public void AfterRecording(JournalEntry entry) => Pass(entry, null);

    public void AfterRegistration(string registration) => Pass(null, registration);

    public Action Deferred(Action action) => () =>
    {
        Interlocked.Increment(ref _deferredRan);
        action();
    };

    public Action<ChangeSet> Deferred(Action<ChangeSet> observer) => changes =>
    {
        Interlocked.Increment(ref _deferredRan);
        observer(changes);
    };

    private void Pass(JournalEntry? entry, string? registration)
    {
        var point = Interlocked.Increment(ref _points);
        if (logPoints)
        {
            Log.Add(Describe(entry, registration));
        }

        if (point == failAt)
        {
            FailedPoint = Describe(entry, registration);
            throw new InjectedFailureException(point, FailedPoint);
        }
    }

    /// <summary>Describes a failure point for a report.</summary>
    private static string Describe(JournalEntry? entry, string? registration)
    {
        if (registration is not null)
        {
            return registration + " registration";
        }

        return entry!.Owner switch
        {
            null => "compensation of an Effect or a RewindFile operation",
            var owner when entry.MemberName is { } member => $"write to {TypeName(owner.GetType())}.{member}",
            var owner when owner.GetType().IsGenericType && owner.GetType().GetGenericTypeDefinition() == typeof(RewindCell<>) => $"write to {TypeName(owner.GetType())}",
            var owner => $"change of {TypeName(owner.GetType())}",
        };
    }

    /// <summary>Returns the name of <paramref name="type"/> as C# writes it, without namespaces: <c>RewindList&lt;String&gt;</c>.</summary>
    private static string TypeName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = new StringBuilder(type.Name, 0, type.Name.IndexOf('`', StringComparison.Ordinal), 32).Append('<');
        var arguments = type.GetGenericArguments();
        for (var index = 0; index < arguments.Length; index++)
        {
            name.Append(index == 0 ? string.Empty : ", ").Append(TypeName(arguments[index]));
        }

        return name.Append('>').ToString();
    }
}

/// <summary>The failure <see cref="RewindVerify"/> injects at a failure point.</summary>
internal sealed class InjectedFailureException(int point, string description)
    : Exception($"RewindVerify made the operation fail at failure point {point}: {description}.");
