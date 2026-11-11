namespace ViShap.Rewind.Generator.Tests.Fixtures;

/// <summary>Compares generated source with the reviewed snapshot in <c>Snapshots/</c>, byte for byte.</summary>
public static class Snapshots
{
    private static readonly string s_directory = Path.Combine(FindProjectDirectory(), "Snapshots");

    /// <summary>
    /// Asserts that <paramref name="actual"/> equals the snapshot <paramref name="name"/>. When they differ, writes the
    /// actual text next to it as <c>.received.txt</c> for review and fails; a snapshot is changed only after review.
    /// </summary>
    public static void Verify(string name, string actual)
    {
        var expectedPath = Path.Combine(s_directory, name + ".g.cs.txt");
        var receivedPath = Path.Combine(s_directory, name + ".g.cs.received.txt");
        var expected = File.Exists(expectedPath) ? File.ReadAllText(expectedPath) : null;

        if (expected == actual)
        {
            File.Delete(receivedPath);
            return;
        }

        File.WriteAllText(receivedPath, actual);
        Assert.Fail(expected is null
            ? $"No snapshot {name}.g.cs.txt; the generated source was written to {receivedPath} for review."
            : $"The generated source differs from {name}.g.cs.txt; it was written to {receivedPath} for review.");
    }

    private static string FindProjectDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ViShap.Rewind.Generator.Tests.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The generator test project directory was not found.");
    }
}
