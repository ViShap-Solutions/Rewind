namespace ViShap.Rewind.Tests.Fixtures;

/// <summary>Locates files of the repository the tests were built from.</summary>
public static class Repository
{
    private const string Marker = "Rewind.slnx";

    /// <summary>Gets the repository root: the nearest directory above the test binaries that holds the solution file.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Returns the full path of <paramref name="relativePath"/>, given with forward slashes from the root.</summary>
    public static string PathOf(string relativePath) =>
        Path.Combine([Root, .. relativePath.Split('/')]);

    /// <summary>Returns <paramref name="fullPath"/> relative to the root, with forward slashes.</summary>
    public static string Relative(string fullPath) =>
        Path.GetRelativePath(Root, fullPath).Replace('\\', '/');

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, Marker)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No directory above {AppContext.BaseDirectory} holds {Marker}.");
    }
}
