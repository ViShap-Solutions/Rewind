namespace ViShap.Rewind;

/// <summary>
/// The outermost scope committed, and one or more effects registered with <see cref="RewindScope.AfterCommit"/>
/// threw. The state stays committed; every other effect was still run.
/// </summary>
public sealed class RewindEffectException : RewindException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="failures">The exceptions thrown by the effects, in the order they were thrown.</param>
    public RewindEffectException(IReadOnlyList<Exception> failures)
        : base("The scope was committed, but effects registered with AfterCommit failed; see Failures.", failures is { Count: > 0 } ? failures[0] : null)
    {
        Failures = failures ?? throw new ArgumentNullException(nameof(failures));
    }

    /// <summary>Gets the exceptions thrown by the effects, in the order they were thrown.</summary>
    public IReadOnlyList<Exception> Failures { get; }
}
