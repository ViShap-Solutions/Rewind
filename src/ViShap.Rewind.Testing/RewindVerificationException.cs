namespace ViShap.Rewind.Testing;

/// <summary>
/// Thrown by <see cref="RewindVerify"/> when an operation is not atomic: after a failure it leaves state that differs
/// from where it started, it runs a deferred effect although it failed, it does not run the same way twice, or a retry
/// of it disagrees with an earlier attempt.
/// </summary>
public sealed class RewindVerificationException : Exception
{
    internal RewindVerificationException(string message, int failurePoint, int failurePointCount, string? point, Exception? innerException)
        : base(message, innerException)
    {
        FailurePoint = failurePoint;
        FailurePointCount = failurePointCount;
        Point = point;
    }

    /// <summary>
    /// Gets the failure point at which the operation was made to fail, counted from 1; 0 when the finding does not
    /// depend on an injected failure.
    /// </summary>
    public int FailurePoint { get; }

    /// <summary>Gets how many failure points the operation has: tracked writes, collection changes, compensations and effect registrations.</summary>
    public int FailurePointCount { get; }

    /// <summary>
    /// Gets what happened at <see cref="FailurePoint"/> — such as <c>write to Order.Status</c> or
    /// <c>AfterCommit registration</c> — or <see langword="null"/> when <see cref="FailurePoint"/> is 0.
    /// </summary>
    public string? Point { get; }
}
