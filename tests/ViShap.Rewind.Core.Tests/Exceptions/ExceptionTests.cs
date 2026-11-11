namespace ViShap.Rewind.Tests.Exceptions;

public sealed class ExceptionTests
{
    [Fact]
    public void Dispose_CompensationThrows_ThrowsCompensationWithoutOriginal()
    {
        var cell = new RewindCell<int>(1);
        var broken = new InvalidOperationException("undo failed");
        var scope = Rewind.Begin();
        cell.Value = 2;
        scope.Effect(() => { }, () => throw broken);

        var thrown = Assert.Throws<RewindCompensationException>(scope.Dispose);

        Assert.Null(thrown.OriginalException);
        Assert.Same(broken, Assert.Single(thrown.Failures));
        Assert.Equal(1, cell.Value);
        Assert.False(scope.IsActive);
        Assert.Null(Rewind.Current);
    }

    [Fact]
    public void Run_SeveralAfterCommitEffectsThrow_InnerExceptionIsFirstFailure()
    {
        var first = new InvalidOperationException("first");
        var second = new InvalidOperationException("second");

        var thrown = Assert.Throws<RewindEffectException>(() => Rewind.Run(scope =>
        {
            scope.AfterCommit(() => throw first);
            scope.AfterCommit(() => throw second);
        }));

        Assert.Same(first, thrown.InnerException);
        Assert.Equal(new Exception[] { first, second }, thrown.Failures);
    }

    [Fact]
    public void Exceptions_EveryLibraryException_DerivesFromRewindException()
    {
        var exceptionTypes = typeof(Rewind).Assembly.GetTypes()
            .Where(type => typeof(Exception).IsAssignableFrom(type))
            .ToList();

        Assert.NotEmpty(exceptionTypes);
        Assert.All(exceptionTypes, type => Assert.True(typeof(RewindException).IsAssignableFrom(type), type.FullName));
    }

    [Fact]
    public void RewindException_IsAbstract()
    {
        Assert.True(typeof(RewindException).IsAbstract);
    }
}
