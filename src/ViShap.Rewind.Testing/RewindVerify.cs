using ViShap.Rewind.Engine;

namespace ViShap.Rewind.Testing;

/// <summary>
/// Proves that an operation using ViShap.Rewind is atomic: that it leaves no trace when it fails at any of its failure
/// points, and that it does the same when it is retried.
/// </summary>
/// <remarks>
/// <para>
/// A failure point is a tracked write recorded by a scope (the first write of a cell or a property in the scope), a
/// change of a Rewind collection, the compensation recorded by <see cref="RewindScope.Effect"/> or a
/// <see cref="RewindFile"/> operation, and the registration of an <c>AfterCommit</c>, <c>AfterRollback</c> or
/// <c>OnCommitted</c> effect.
/// </para>
/// <para>
/// The operation runs as written: the outermost scopes it opens are its own, and they commit or roll back for real.
/// Every scope it opens in its own flow — also on another thread, when the flow reaches it — is observed; a scope opened
/// in a flow the operation did not start, such as a thread created with the execution context suppressed, is not. A
/// failed check is a <see cref="RewindVerificationException"/>, so the methods work with any test framework.
/// </para>
/// </remarks>
public static class RewindVerify
{
    /// <summary>
    /// Runs <paramref name="operation"/> once to count its failure points, then once more for each of them, making it fail
    /// there; after every run, checks that <paramref name="capture"/> returns what it returned before the first run, and
    /// that no deferred effect ran.
    /// </summary>
    /// <typeparam name="TState">The type of the captured state, compared with its default equality.</typeparam>
    /// <param name="operation">The operation, as the application calls it: it opens its own scopes.</param>
    /// <param name="capture">Returns the state the operation may change — tracked and untracked, and fakes of external systems.</param>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> or <paramref name="capture"/> is <see langword="null"/>.</exception>
    /// <exception cref="RewindScopeException">A scope is open: the operation must run as outermost code.</exception>
    /// <exception cref="RewindVerificationException">The operation is not atomic, or failed without an injected failure.</exception>
    /// <remarks>
    /// An operation that catches the injected failure and completes passes the check when the state is as it was and no
    /// deferred effect ran.
    /// </remarks>
    public static void Rollback<TState>(Action operation, Func<TState> capture) =>
        Rollback(operation, capture, EqualityComparer<TState>.Default);

    /// <summary>
    /// Runs <paramref name="operation"/> once to count its failure points, then once more for each of them, making it fail
    /// there; after every run, checks with <paramref name="comparer"/> that <paramref name="capture"/> returns what it
    /// returned before the first run, and that no deferred effect ran.
    /// </summary>
    /// <typeparam name="TState">The type of the captured state.</typeparam>
    /// <param name="operation">The operation, as the application calls it: it opens its own scopes.</param>
    /// <param name="capture">Returns the state the operation may change — tracked and untracked, and fakes of external systems.</param>
    /// <param name="comparer">Compares two captured states.</param>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/>, <paramref name="capture"/> or <paramref name="comparer"/> is <see langword="null"/>.</exception>
    /// <exception cref="RewindScopeException">A scope is open: the operation must run as outermost code.</exception>
    /// <exception cref="RewindVerificationException">The operation is not atomic, or failed without an injected failure.</exception>
    /// <remarks>
    /// An operation that catches the injected failure and completes passes the check when the state is as it was and no
    /// deferred effect ran.
    /// </remarks>
    public static void Rollback<TState>(Action operation, Func<TState> capture, IEqualityComparer<TState> comparer)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(comparer);
        EnsureOutermost();

        var start = capture();
        var counting = new Injector(failAt: 0, logPoints: false);
        FaultInjection.Arm(counting);
        try
        {
            using (Rewind.Begin())
            {
                operation();
            }
        }
        catch (Exception exception)
        {
            throw FailedOnItsOwn(exception);
        }
        finally
        {
            FaultInjection.Disarm();
        }

        var count = counting.Points;
        CheckRestored(start, capture(), comparer, count);
        for (var point = 1; point <= count; point++)
        {
            var injector = new Injector(point, logPoints: false);
            Exception? failure = null;
            FaultInjection.Arm(injector);
            try
            {
                operation();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                FaultInjection.Disarm();
            }

            Judge(point, count, injector, failure, start, capture(), comparer);
        }
    }

    /// <summary>
    /// Runs the asynchronous <paramref name="operation"/> once to count its failure points, then once more for each of
    /// them, making it fail there; after every run, checks that <paramref name="capture"/> returns what it returned before
    /// the first run, and that no deferred effect ran.
    /// </summary>
    /// <typeparam name="TState">The type of the captured state, compared with its default equality.</typeparam>
    /// <param name="operation">The operation, as the application calls it: it opens its own scopes.</param>
    /// <param name="capture">Returns the state the operation may change — tracked and untracked, and fakes of external systems.</param>
    /// <returns>A task that completes when every check passed, or faults with the first that did not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> or <paramref name="capture"/> is <see langword="null"/>.</exception>
    /// <exception cref="RewindScopeException">A scope is open: the operation must run as outermost code.</exception>
    /// <exception cref="RewindVerificationException">The operation is not atomic, or failed without an injected failure.</exception>
    /// <remarks>
    /// An operation that catches the injected failure and completes passes the check when the state is as it was and no
    /// deferred effect ran.
    /// </remarks>
    public static Task RollbackAsync<TState>(Func<Task> operation, Func<TState> capture) =>
        RollbackAsync(operation, capture, EqualityComparer<TState>.Default);

    /// <summary>
    /// Runs the asynchronous <paramref name="operation"/> once to count its failure points, then once more for each of
    /// them, making it fail there; after every run, checks with <paramref name="comparer"/> that <paramref name="capture"/>
    /// returns what it returned before the first run, and that no deferred effect ran.
    /// </summary>
    /// <typeparam name="TState">The type of the captured state.</typeparam>
    /// <param name="operation">The operation, as the application calls it: it opens its own scopes.</param>
    /// <param name="capture">Returns the state the operation may change — tracked and untracked, and fakes of external systems.</param>
    /// <param name="comparer">Compares two captured states.</param>
    /// <returns>A task that completes when every check passed, or faults with the first that did not.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/>, <paramref name="capture"/> or <paramref name="comparer"/> is <see langword="null"/>.</exception>
    /// <exception cref="RewindScopeException">A scope is open: the operation must run as outermost code.</exception>
    /// <exception cref="RewindVerificationException">The operation is not atomic, or failed without an injected failure.</exception>
    /// <remarks>
    /// An operation that catches the injected failure and completes passes the check when the state is as it was and no
    /// deferred effect ran.
    /// </remarks>
    public static Task RollbackAsync<TState>(Func<Task> operation, Func<TState> capture, IEqualityComparer<TState> comparer)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(comparer);
        EnsureOutermost();
        return RollbackCoreAsync(operation, capture, comparer);
    }

    /// <summary>
    /// Runs <paramref name="operation"/> <paramref name="times"/> times from the same starting state, rolling each attempt
    /// back, and checks that every attempt returns the same result, leaves the same state, and passes the same failure
    /// points — records the same writes and registers the same effects — as the first, and that each rollback restores the
    /// starting state.
    /// </summary>
    /// <typeparam name="TResult">The type of the operation's result, compared with its default equality.</typeparam>
    /// <typeparam name="TState">The type of the captured state, compared with its default equality.</typeparam>
    /// <param name="operation">The operation, as the application calls it, and would call it again after a failure.</param>
    /// <param name="times">How many attempts to run; at least 2.</param>
    /// <param name="capture">Returns the state the operation may change, captured at the end of each attempt and after its rollback.</param>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> or <paramref name="capture"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="times"/> is less than 2.</exception>
    /// <exception cref="RewindScopeException">A scope is open: the operation must run as outermost code.</exception>
    /// <exception cref="RewindVerificationException">An attempt disagrees with the first, leaves state its rollback does not restore, or fails.</exception>
    /// <remarks>Each attempt runs inside a scope that is rolled back, so the deferred effects of an attempt never run.</remarks>
    public static void Retries<TResult, TState>(Func<TResult> operation, int times, Func<TState> capture)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfLessThan(times, 2);
        ArgumentNullException.ThrowIfNull(capture);
        EnsureOutermost();

        var start = capture();
        Attempt<TResult, TState>? first = null;
        for (var attempt = 1; attempt <= times; attempt++)
        {
            var injector = new Injector(failAt: 0, logPoints: true);
            TResult result;
            TState state;
            FaultInjection.Arm(injector);
            try
            {
                using (Rewind.Begin())
                {
                    result = operation();
                    state = capture();
                }
            }
            catch (Exception exception)
            {
                throw AttemptFailed(attempt, exception);
            }
            finally
            {
                FaultInjection.Disarm();
            }

            first = Compare(attempt, new Attempt<TResult, TState>(result, state, injector.Log), first, start, capture());
        }
    }

    /// <summary>
    /// Runs the asynchronous <paramref name="operation"/> <paramref name="times"/> times from the same starting state,
    /// rolling each attempt back, and checks that every attempt returns the same result, leaves the same state, and passes
    /// the same failure points as the first, and that each rollback restores the starting state.
    /// </summary>
    /// <typeparam name="TResult">The type of the operation's result, compared with its default equality.</typeparam>
    /// <typeparam name="TState">The type of the captured state, compared with its default equality.</typeparam>
    /// <param name="operation">The operation, as the application calls it, and would call it again after a failure.</param>
    /// <param name="times">How many attempts to run; at least 2.</param>
    /// <param name="capture">Returns the state the operation may change, captured at the end of each attempt and after its rollback.</param>
    /// <returns>A task that completes when every attempt agreed, or faults with the first disagreement.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> or <paramref name="capture"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="times"/> is less than 2.</exception>
    /// <exception cref="RewindScopeException">A scope is open: the operation must run as outermost code.</exception>
    /// <exception cref="RewindVerificationException">An attempt disagrees with the first, leaves state its rollback does not restore, or fails.</exception>
    /// <remarks>Each attempt runs inside a scope that is rolled back, so the deferred effects of an attempt never run.</remarks>
    public static Task RetriesAsync<TResult, TState>(Func<Task<TResult>> operation, int times, Func<TState> capture)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfLessThan(times, 2);
        ArgumentNullException.ThrowIfNull(capture);
        EnsureOutermost();
        return RetriesCoreAsync(operation, times, capture);
    }

    private static async Task RollbackCoreAsync<TState>(Func<Task> operation, Func<TState> capture, IEqualityComparer<TState> comparer)
    {
        var start = capture();
        var counting = new Injector(failAt: 0, logPoints: false);
        FaultInjection.Arm(counting);
        try
        {
            await Rewind.RunAsync(async () =>
            {
                await operation();
                throw new RolledBackOnPurposeException();
            });
        }
        catch (RolledBackOnPurposeException)
        {
        }
        catch (Exception exception)
        {
            throw FailedOnItsOwn(exception);
        }
        finally
        {
            FaultInjection.Disarm();
        }

        var count = counting.Points;
        CheckRestored(start, capture(), comparer, count);
        for (var point = 1; point <= count; point++)
        {
            var injector = new Injector(point, logPoints: false);
            Exception? failure = null;
            FaultInjection.Arm(injector);
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                FaultInjection.Disarm();
            }

            Judge(point, count, injector, failure, start, capture(), comparer);
        }
    }

    private static async Task RetriesCoreAsync<TResult, TState>(Func<Task<TResult>> operation, int times, Func<TState> capture)
    {
        var start = capture();
        Attempt<TResult, TState>? first = null;
        for (var attempt = 1; attempt <= times; attempt++)
        {
            var injector = new Injector(failAt: 0, logPoints: true);
            TResult result = default!;
            TState state = default!;
            FaultInjection.Arm(injector);
            try
            {
                await Rewind.RunAsync(async () =>
                {
                    result = await operation();
                    state = capture();
                    throw new RolledBackOnPurposeException();
                });
            }
            catch (RolledBackOnPurposeException)
            {
            }
            catch (Exception exception)
            {
                throw AttemptFailed(attempt, exception);
            }
            finally
            {
                FaultInjection.Disarm();
            }

            first = Compare(attempt, new Attempt<TResult, TState>(result, state, injector.Log), first, start, capture());
        }
    }

    private static void EnsureOutermost()
    {
        if (Rewind.Current is not null)
        {
            throw new RewindScopeException("RewindVerify runs the operation as outermost code; call it with no scope open.");
        }
    }

    private static void CheckRestored<TState>(TState start, TState afterCounting, IEqualityComparer<TState> comparer, int count)
    {
        if (!comparer.Equals(start, afterCounting))
        {
            throw new RewindVerificationException(
                "The operation changes state that a rollback does not restore: run inside a scope that rolled back, it left the state different."
                + States(start, afterCounting),
                0,
                count,
                null,
                null);
        }
    }

    private static void Judge<TState>(int point, int count, Injector injector, Exception? failure, TState start, TState state, IEqualityComparer<TState> comparer)
    {
        if (injector.FailedPoint is not { } described)
        {
            var reason = injector.Points == 0
                ? "it passed no failure point when run on its own: it writes tracked state with no scope of its own open"
                : $"it passed {injector.Points} of {count} failure points when run on its own: it does not run the same way twice";
            throw new RewindVerificationException($"The operation could not be made to fail at failure point {point} of {count}: {reason}.", point, count, null, failure);
        }

        var caught = failure is null ? " The operation caught the failure and completed." : string.Empty;
        if (!comparer.Equals(start, state))
        {
            throw new RewindVerificationException(
                $"After a failure at point {point} of {count} ({described}), the state is not as it was.{caught}" + States(start, state),
                point,
                count,
                described,
                failure);
        }

        if (injector.DeferredRan > 0)
        {
            throw new RewindVerificationException(
                $"After a failure at point {point} of {count} ({described}), {injector.DeferredRan} deferred effect(s) ran: a scope of the operation committed before it failed.{caught}",
                point,
                count,
                described,
                failure);
        }
    }

    private static Attempt<TResult, TState> Compare<TResult, TState>(int attempt, Attempt<TResult, TState> current, Attempt<TResult, TState>? first, TState start, TState afterRollback)
    {
        if (!EqualityComparer<TState>.Default.Equals(start, afterRollback))
        {
            throw new RewindVerificationException(
                $"Attempt {attempt} left state that its rollback does not restore, so a retry would start from different state." + States(start, afterRollback),
                0,
                current.Points.Count,
                null,
                null);
        }

        if (first is null)
        {
            return current;
        }

        if (!EqualityComparer<TResult>.Default.Equals(first.Result, current.Result))
        {
            throw Disagreement(attempt, current, $"returned {Show(current.Result)}, the first attempt returned {Show(first.Result)}");
        }

        if (!EqualityComparer<TState>.Default.Equals(first.State, current.State))
        {
            throw Disagreement(attempt, current, $"ended with state {Show(current.State)}, the first attempt with {Show(first.State)}");
        }

        for (var index = 0; index < Math.Max(first.Points.Count, current.Points.Count); index++)
        {
            var mine = index < current.Points.Count ? current.Points[index] : "nothing";
            var theirs = index < first.Points.Count ? first.Points[index] : "nothing";
            if (mine != theirs)
            {
                throw Disagreement(attempt, current, $"passed {mine} at failure point {index + 1}, the first attempt {theirs}");
            }
        }

        return first;
    }

    private static RewindVerificationException Disagreement<TResult, TState>(int attempt, Attempt<TResult, TState> current, string difference) =>
        new($"Attempt {attempt} disagrees with the first: it {difference}.", 0, current.Points.Count, null, null);

    private static RewindVerificationException FailedOnItsOwn(Exception exception) =>
        new("The operation failed when run without an injected failure; see the inner exception.", 0, 0, null, exception);

    private static RewindVerificationException AttemptFailed(int attempt, Exception exception) =>
        new($"Attempt {attempt} of the operation failed; see the inner exception.", 0, 0, null, exception);

    private static string States<TState>(TState expected, TState actual) =>
        $"{Environment.NewLine}Expected: {Show(expected)}{Environment.NewLine}Actual:   {Show(actual)}";

    private static string Show<T>(T value) => value?.ToString() ?? "null";

    /// <summary>What one attempt of <see cref="Retries"/> produced.</summary>
    private sealed record Attempt<TResult, TState>(TResult Result, TState State, List<string> Points);

    /// <summary>Ends a run that <see cref="RewindVerify"/> rolls back on purpose.</summary>
    private sealed class RolledBackOnPurposeException : Exception;
}
