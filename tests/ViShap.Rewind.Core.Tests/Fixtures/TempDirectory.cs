namespace ViShap.Rewind.Tests.Fixtures;

/// <summary>A directory of its own under the system temporary directory, deleted with everything in it on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    /// <summary>Creates the directory.</summary>
    public TempDirectory()
    {
        Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), "rewind-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Gets the full path of the directory.</summary>
    public string Path { get; }

    /// <summary>Returns the full path of <paramref name="name"/> inside the directory.</summary>
    public string File(string name) => System.IO.Path.Join(Path, name);

    /// <summary>Returns the names of the side files <c>RewindFile</c> left in the directory.</summary>
    public string[] SideFiles() =>
        Directory.GetFiles(Path, "*.rewind-*").Select(System.IO.Path.GetFileName).ToArray()!;

    /// <summary>Returns the names of every file in the directory, in ordinal order.</summary>
    public string[] Files() =>
        Directory.GetFiles(Path).Select(System.IO.Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    /// <summary>Deletes the directory and its content.</summary>
    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
