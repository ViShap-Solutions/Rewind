namespace ViShap.Rewind.Tests.Fixtures;

public sealed class FixtureTests
{
    [Fact]
    public void Measure_ActionAllocatesNothing_ReturnsZero()
    {
        var bytes = Allocations.Measure(static () => { });

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void Measure_ActionAllocates_ReturnsBytes()
    {
        var bytes = Allocations.Measure(static () => GC.KeepAlive(new object()));

        Assert.True(bytes > 0);
    }

    [Fact]
    public void Run_ExecutesOnAnotherThread()
    {
        var current = Environment.CurrentManagedThreadId;

        var other = OtherThread.Run(() => Environment.CurrentManagedThreadId);

        Assert.NotEqual(current, other);
    }

    [Fact]
    public void TempDirectory_Dispose_DeletesDirectoryAndContent()
    {
        var directory = new TempDirectory();
        File.WriteAllText(directory.File("a.txt"), "a");
        File.WriteAllText(directory.File(".a.txt.0.rewind-bak"), "a");

        Assert.Equal(new[] { ".a.txt.0.rewind-bak", "a.txt" }, directory.Files());
        Assert.Equal(new[] { ".a.txt.0.rewind-bak" }, directory.SideFiles());
        directory.Dispose();

        Assert.False(Directory.Exists(directory.Path));
    }

    [Fact]
    public void Capture_ActionThrows_ReturnsException()
    {
        var thrown = new InvalidOperationException();

        var captured = OtherThread.Capture(() => throw thrown);

        Assert.Same(thrown, captured);
    }
}
