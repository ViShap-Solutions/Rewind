namespace ViShap.Rewind;

/// <summary>
/// An operation that <see cref="Rewind.Run{TOperation}(TOperation)"/> runs in a scope. Implement it on a struct to
/// run a domain operation without allocating a delegate.
/// </summary>
public interface IRewindOperation
{
    /// <summary>Performs the operation. Throwing rolls back every change it made to tracked state.</summary>
    void Execute();
}
