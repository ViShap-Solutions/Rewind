namespace ViShap.Rewind;

/// <summary>
/// A rollback completed, but one or more compensations or rollback effects threw. Tracked state is restored; the
/// external results of the failed compensations may not be.
/// </summary>
public sealed class RewindCompensationException : RewindException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="originalException">The exception that caused the rollback, or <see langword="null"/> when a scope was disposed without a commit.</param>
    /// <param name="failures">The exceptions thrown by compensations and rollback effects, in the order they were thrown.</param>
    public RewindCompensationException(Exception? originalException, IReadOnlyList<Exception> failures)
        : base("The scope was rolled back, but compensations or rollback effects failed; see Failures.", originalException)
    {
        ArgumentNullException.ThrowIfNull(failures);
        Failures = failures;
    }

    /// <summary>Gets the exception that caused the rollback, or <see langword="null"/> when a scope was disposed without a commit.</summary>
    public Exception? OriginalException => InnerException;

    /// <summary>Gets the exceptions thrown by compensations and rollback effects, in the order they were thrown.</summary>
    public IReadOnlyList<Exception> Failures { get; }
}
