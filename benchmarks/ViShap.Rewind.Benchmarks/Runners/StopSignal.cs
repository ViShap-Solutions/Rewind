namespace ViShap.Rewind.Benchmarks.Runners;

/// <summary>
/// The operator's request to end a track run early. The first Ctrl+C, or a <c>STOP</c> file in the run's
/// directory, asks the run to finish the suite it is in and stop; a second Ctrl+C stops at once.
/// </summary>
/// <remarks>
/// Ctrl+C is read as a key rather than received as a signal. A signal reaches every process on the
/// console — the BenchmarkDotNet child measuring the current cell among them — and BenchmarkDotNet's
/// own handler kills that child, so a signal could never mean "finish the suite". With
/// <see cref="Console.TreatControlCAsInput"/> set, no process on the console gets a signal, and the run
/// decides what the key means. When standard input is not a console the key cannot be read; Ctrl+C then
/// keeps its usual meaning, and the <c>STOP</c> file is the way to ask for a stop.
/// </remarks>
internal sealed class StopSignal : IDisposable
{
    /// <summary>The exit code of a run stopped at once, as a shell reports a process ended by Ctrl+C.</summary>
    internal const int ImmediateExitCode = 130;

    private readonly string stopFile;
    private readonly Action immediate;
    private readonly bool consoleTaken;
    private int presses;

    private StopSignal(string stopFile, Action immediate)
    {
        this.stopFile = stopFile;
        this.immediate = immediate;

        try
        {
            if (!Console.IsInputRedirected)
            {
                Console.TreatControlCAsInput = true;
                consoleTaken = true;
            }
        }
        catch (IOException)
        {
            consoleTaken = false;
        }

        if (consoleTaken)
        {
            AppDomain.CurrentDomain.ProcessExit += Restore;
            new Thread(ReadKeys) { IsBackground = true, Name = "Ctrl+C listener" }.Start();
        }
    }

    /// <summary>True once the operator asked for the run to stop after the current suite.</summary>
    internal bool Requested => Volatile.Read(ref presses) > 0 || File.Exists(stopFile);

    /// <summary>How the stop was asked for, for the run's record and the console.</summary>
    internal string How => Volatile.Read(ref presses) > 0 ? "Ctrl+C" : $"the file {Path.GetFileName(stopFile)}";

    /// <summary>True when Ctrl+C is read by the run; otherwise only the <c>STOP</c> file asks for a stop.</summary>
    internal bool ReadsControlC => consoleTaken;

    /// <summary>
    /// Starts listening. <paramref name="immediate"/> runs on the second Ctrl+C, after the console is
    /// given back, and is expected to end the process.
    /// </summary>
    internal static StopSignal Listen(string directory, Action immediate) =>
        new(Path.Combine(directory, "STOP"), immediate);

    public void Dispose()
    {
        Restore(null, EventArgs.Empty);
        AppDomain.CurrentDomain.ProcessExit -= Restore;
    }

    private void ReadKeys()
    {
        while (true)
        {
            ConsoleKeyInfo key;

            try
            {
                key = Console.ReadKey(intercept: true);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (key.Key != ConsoleKey.C || (key.Modifiers & ConsoleModifiers.Control) == 0)
            {
                continue;
            }

            if (Interlocked.Increment(ref presses) == 1)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "── Ctrl+C: the run stops when the current suite finishes, and writes what it measured. " +
                    "Press Ctrl+C again to stop at once.");

                continue;
            }

            Restore(null, EventArgs.Empty);
            immediate();
            return;
        }
    }

    /// <summary>
    /// Gives Ctrl+C back to the console. The console's mode outlives the process on Windows, so a shell
    /// would otherwise inherit a console in which Ctrl+C is a character.
    /// </summary>
    private void Restore(object? sender, EventArgs e)
    {
        if (!consoleTaken)
        {
            return;
        }

        try
        {
            Console.TreatControlCAsInput = false;
        }
        catch (IOException)
        {
            // The console is gone; there is nothing left to give back.
        }
    }
}
