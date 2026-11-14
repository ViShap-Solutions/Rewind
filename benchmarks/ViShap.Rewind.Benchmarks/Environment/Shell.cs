using System.Diagnostics;

namespace ViShap.Rewind.Benchmarks.Environment;

/// <summary>
/// Reads a fact from the host by running a program. Every call is best-effort: a missing tool or a non-zero exit
/// yields an empty string rather than an exception, because a manifest field that cannot be captured is recorded as
/// unknown and never stops a run.
/// </summary>
internal static class Shell
{
    internal static string Git(string arguments) => Run("git", arguments);

    internal static string Run(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = Paths.ProjectDirectory,
            });

            if (process is null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);

            return process.ExitCode == 0 ? output.Trim() : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
