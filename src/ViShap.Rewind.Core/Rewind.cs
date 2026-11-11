namespace ViShap.Rewind;

/// <summary>
/// The entry point of the library. Runs a block of code as an atomic unit of in-memory state change: when the
/// block completes normally its changes to tracked state are kept; when it throws, they are undone and the
/// exception reaches the caller.
/// </summary>
/// <remarks>
/// Tracked state is state the library can restore: a <see cref="RewindCell{T}"/> and the members built on it.
/// A plain field or property is not tracked and keeps whatever value the block left in it.
/// </remarks>
public static class Rewind
{
    /// <summary>Gets the innermost scope open on the current thread, or <see langword="null"/> when none is open.</summary>
    /// <remarks>
    /// It is also <see langword="null"/> while a rollback runs and while effects registered with
    /// <see cref="RewindScope.AfterCommit"/> or <see cref="RewindScope.AfterRollback"/> run.
    /// </remarks>
    public static RewindScope? Current => ScopeContext.Current;

    /// <summary>
    /// Opens a scope on the current thread. Disposing it without calling <see cref="RewindScope.Commit"/>
    /// undoes every change made to tracked state while it was open.
    /// </summary>
    /// <returns>The new scope, now the innermost scope of the current thread.</returns>
    public static RewindScope Begin() => RewindScope.Open(managed: false);

    /// <summary>Runs <paramref name="action"/> in a new scope; commits when it returns, rolls back when it throws.</summary>
    /// <param name="action">The block to run.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="RewindCompensationException">The block threw and a compensation or a rollback effect also threw.</exception>
    /// <exception cref="RewindEffectException">The state was committed and an effect registered with <see cref="RewindScope.AfterCommit"/> threw.</exception>
    /// <exception cref="RewindScopeException">The block left a scope it began open.</exception>
    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var scope = RewindScope.Open(managed: true);
        try
        {
            action();
        }
        catch (Exception exception)
        {
            var failure = scope.Fail(exception);
            if (failure is null)
            {
                throw;
            }

            throw failure;
        }

        scope.CompleteManaged();
    }

    /// <summary>Runs <paramref name="action"/> in a new scope and hands it the scope, for registering effects.</summary>
    /// <param name="action">The block to run. It must not commit or dispose the scope it receives.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="RewindCompensationException">The block threw and a compensation or a rollback effect also threw.</exception>
    /// <exception cref="RewindEffectException">The state was committed and an effect registered with <see cref="RewindScope.AfterCommit"/> threw.</exception>
    /// <exception cref="RewindScopeException">The block left a scope it began open.</exception>
    public static void Run(Action<RewindScope> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var scope = RewindScope.Open(managed: true);
        try
        {
            action(scope);
        }
        catch (Exception exception)
        {
            var failure = scope.Fail(exception);
            if (failure is null)
            {
                throw;
            }

            throw failure;
        }

        scope.CompleteManaged();
    }

    /// <summary>Runs <paramref name="action"/> with <paramref name="state"/> in a new scope, so the block can be a static lambda that captures nothing.</summary>
    /// <typeparam name="TState">The type of the state handed to the block.</typeparam>
    /// <param name="state">The state handed to the block.</param>
    /// <param name="action">The block to run.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="RewindCompensationException">The block threw and a compensation or a rollback effect also threw.</exception>
    /// <exception cref="RewindEffectException">The state was committed and an effect registered with <see cref="RewindScope.AfterCommit"/> threw.</exception>
    /// <exception cref="RewindScopeException">The block left a scope it began open.</exception>
    public static void Run<TState>(TState state, Action<TState> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var scope = RewindScope.Open(managed: true);
        try
        {
            action(state);
        }
        catch (Exception exception)
        {
            var failure = scope.Fail(exception);
            if (failure is null)
            {
                throw;
            }

            throw failure;
        }

        scope.CompleteManaged();
    }

    /// <summary>Runs <paramref name="operation"/> in a new scope. A struct operation is run without allocating a delegate or boxing.</summary>
    /// <typeparam name="TOperation">The type of the operation.</typeparam>
    /// <param name="operation">The operation to run.</param>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is a <see langword="null"/> reference.</exception>
    /// <exception cref="RewindCompensationException">The operation threw and a compensation or a rollback effect also threw.</exception>
    /// <exception cref="RewindEffectException">The state was committed and an effect registered with <see cref="RewindScope.AfterCommit"/> threw.</exception>
    /// <exception cref="RewindScopeException">The operation left a scope it began open.</exception>
    public static void Run<TOperation>(TOperation operation)
        where TOperation : IRewindOperation
    {
        if (!typeof(TOperation).IsValueType && operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        var scope = RewindScope.Open(managed: true);
        try
        {
            operation.Execute();
        }
        catch (Exception exception)
        {
            var failure = scope.Fail(exception);
            if (failure is null)
            {
                throw;
            }

            throw failure;
        }

        scope.CompleteManaged();
    }

    /// <summary>Runs <paramref name="func"/> in a new scope and returns its result once the scope has committed.</summary>
    /// <typeparam name="TResult">The type of the result. An awaitable type is refused.</typeparam>
    /// <param name="func">The block to run.</param>
    /// <returns>The value <paramref name="func"/> returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="func"/> is <see langword="null"/>.</exception>
    /// <exception cref="RewindScopeException"><typeparamref name="TResult"/> is awaitable, so the block would outlive its scope; or the block left a scope it began open.</exception>
    /// <exception cref="RewindCompensationException">The block threw and a compensation or a rollback effect also threw.</exception>
    /// <exception cref="RewindEffectException">The state was committed and an effect registered with <see cref="RewindScope.AfterCommit"/> threw.</exception>
    public static TResult Run<TResult>(Func<TResult> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        AwaitableShape<TResult>.ThrowIfAwaitable();
        var scope = RewindScope.Open(managed: true);
        TResult result;
        try
        {
            result = func();
        }
        catch (Exception exception)
        {
            var failure = scope.Fail(exception);
            if (failure is null)
            {
                throw;
            }

            throw failure;
        }

        scope.CompleteManaged();
        return result;
    }

    /// <summary>Runs <paramref name="func"/> in a new scope, hands it the scope, and returns its result once the scope has committed.</summary>
    /// <typeparam name="TResult">The type of the result. An awaitable type is refused.</typeparam>
    /// <param name="func">The block to run. It must not commit or dispose the scope it receives.</param>
    /// <returns>The value <paramref name="func"/> returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="func"/> is <see langword="null"/>.</exception>
    /// <exception cref="RewindScopeException"><typeparamref name="TResult"/> is awaitable, so the block would outlive its scope; or the block left a scope it began open.</exception>
    /// <exception cref="RewindCompensationException">The block threw and a compensation or a rollback effect also threw.</exception>
    /// <exception cref="RewindEffectException">The state was committed and an effect registered with <see cref="RewindScope.AfterCommit"/> threw.</exception>
    public static TResult Run<TResult>(Func<RewindScope, TResult> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        AwaitableShape<TResult>.ThrowIfAwaitable();
        var scope = RewindScope.Open(managed: true);
        TResult result;
        try
        {
            result = func(scope);
        }
        catch (Exception exception)
        {
            var failure = scope.Fail(exception);
            if (failure is null)
            {
                throw;
            }

            throw failure;
        }

        scope.CompleteManaged();
        return result;
    }
}
