namespace ViShap.Rewind.Tests.Fixtures;

/// <summary>Runs code on a dedicated thread and waits for it.</summary>
public static class OtherThread
{
    /// <summary>Runs <paramref name="func"/> on a new thread and returns its result.</summary>
    public static T Run<T>(Func<T> func)
    {
        T result = default!;
        var thread = new Thread(() => result = func());
        thread.Start();
        thread.Join();
        return result;
    }

    /// <summary>Runs <paramref name="action"/> on a new thread and returns the exception it threw, or <see langword="null"/>.</summary>
    public static Exception? Capture(Action action) => Run(() => Record.Exception(action));
}
