namespace ViShap.Rewind;

/// <summary>The base of every exception the library raises on its own account.</summary>
public abstract class RewindException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    private protected RewindException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The exception that caused it.</param>
    private protected RewindException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
