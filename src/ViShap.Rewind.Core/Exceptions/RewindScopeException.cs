namespace ViShap.Rewind;

/// <summary>
/// A scope was used in a way its rules forbid: on another thread, after it closed, out of nesting order, or by a
/// block that returns an awaitable result.
/// </summary>
public sealed class RewindScopeException : RewindException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Which rule was broken.</param>
    public RewindScopeException(string message)
        : base(message)
    {
    }
}
