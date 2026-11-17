namespace ViShap.Rewind.Engine;

/// <summary>Whether a result type is awaitable: a block returning one would keep running after its scope closed.</summary>
internal static class AwaitableShape<T>
{
    private static readonly bool s_isAwaitable = Compute();

    public static void ThrowIfAwaitable()
    {
        if (s_isAwaitable)
        {
            throw new RewindScopeException($"Rewind.Run does not accept a block returning {typeof(T).Name}: the block would continue after its scope closed. Run the synchronous part in the scope, or use Rewind.RunAsync.");
        }
    }

    private static bool Compute()
    {
        var type = typeof(T);
        if (typeof(Task).IsAssignableFrom(type) || type == typeof(ValueTask))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>);
    }
}
