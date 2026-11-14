namespace ViShap.Rewind.Benchmarks.Environment;

/// <summary>
/// Where the harness reads from and writes to. Results land beside the benchmark project rather than in whatever
/// directory the run was started from, so a run is reproducible from anywhere.
/// </summary>
internal static class Paths
{
    /// <summary>The project path as a command line names it, for the instructions a run writes.</summary>
    internal const string ProjectName = "benchmarks/ViShap.Rewind.Benchmarks";

    private const string ProjectFile = "ViShap.Rewind.Benchmarks.csproj";

    /// <summary>The benchmark project directory.</summary>
    internal static string ProjectDirectory { get; } = FindProjectDirectory();

    /// <summary>The project file, read for the packages it references.</summary>
    internal static string ProjectFilePath => Path.Combine(ProjectDirectory, ProjectFile);

    /// <summary>Raw BenchmarkDotNet output of a run started outside a track.</summary>
    internal static string Artifacts { get; } = Path.Combine(ProjectDirectory, "BenchmarkDotNet.Artifacts");

    /// <summary>Frozen baselines, one directory per tag.</summary>
    internal static string Baselines { get; } = Path.Combine(ProjectDirectory, "Baselines");

    /// <summary>Measurements, one directory per run; never committed.</summary>
    internal static string Measurements { get; } = Path.Combine(ProjectDirectory, "Measurements");

    private static string FindProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.GetFiles(ProjectFile).Length > 0)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
