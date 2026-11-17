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

    /// <summary>
    /// Returns an awaitable that always continues the awaiting method on a new thread, carrying its execution context, so
    /// the method resumes on another thread without depending on timing.
    /// </summary>
    public static NewThreadAwaitable Resume() => default;

    /// <summary>An awaitable that is never complete and runs its continuation on a new thread.</summary>
    public readonly struct NewThreadAwaitable : System.Runtime.CompilerServices.INotifyCompletion
    {
        /// <summary>Gets the awaiter: the awaitable itself.</summary>
        public NewThreadAwaitable GetAwaiter() => this;

        /// <summary>Gets <see langword="false"/>: the continuation always moves to a new thread.</summary>
        public bool IsCompleted => false;

        /// <summary>Starts a new thread that runs <paramref name="continuation"/>.</summary>
        public void OnCompleted(Action continuation) => new Thread(() => continuation()).Start();

        /// <summary>Ends the await.</summary>
        public void GetResult()
        {
        }
    }
}
