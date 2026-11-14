using System.Diagnostics;
using System.Globalization;
using System.Text;
using ViShap.Rewind.Benchmarks.Environment;

namespace ViShap.Rewind.Benchmarks.Runners;

/// <summary>
/// Sustained load: a mixed workload of scopes — commits, rollbacks, nesting, collections and effects — run without
/// pause for a fixed duration, with throughput and memory sampled throughout. Growth a per-operation benchmark cannot
/// see — a journal that is not reused, an effect list that keeps its actions — becomes a rising curve here.
/// </summary>
internal static class SoakRunner
{
    private static readonly TimeSpan Sample = TimeSpan.FromSeconds(5);

    internal static int Run(TimeSpan duration, string? outputDirectory)
    {
        var rows = new StringBuilder("elapsed_s,operations,ops_per_second,managed_heap_bytes,working_set_bytes,gen0,gen1,gen2\n");
        var cells = Enumerable.Range(0, 16).Select(_ => new RewindCell<int>(0)).ToArray();
        var list = new RewindList<int>();
        var dictionary = new RewindDictionary<int, int>();
        var counter = 0;
        Action effect = () => counter++;

        var total = Stopwatch.StartNew();
        var window = Stopwatch.StartNew();
        long operations = 0;
        long windowOperations = 0;

        Console.WriteLine($"  soak for {duration.TotalMinutes:F1} min, sampled every {Sample.TotalSeconds:F0} s");

        while (total.Elapsed < duration)
        {
            for (var round = 0; round < 1_000; round++)
            {
                var value = (int)(operations & 0xFFFF);

                // A committed scope over every cell, with an effect.
                using (var scope = Rewind.Begin())
                {
                    foreach (var cell in cells)
                    {
                        cell.Value = value;
                    }

                    scope.AfterCommit(effect);
                    scope.Commit();
                }

                // A rolled-back scope with a nested committed one inside it.
                using (Rewind.Begin())
                {
                    cells[0].Value = -value;

                    using var inner = Rewind.Begin();
                    cells[1].Value = -value;
                    inner.Commit();
                }

                // Collections: a committed append and a rolled-back clear.
                using (var scope = Rewind.Begin())
                {
                    list.Add(value);
                    dictionary[value & 0xFF] = value;
                    scope.Commit();
                }

                using (Rewind.Begin())
                {
                    list.Clear();
                }

                if (list.Count > 4_096)
                {
                    list.Clear();
                }

                operations += 5;
                windowOperations += 5;
            }

            if (window.Elapsed < Sample)
            {
                continue;
            }

            var perSecond = windowOperations / window.Elapsed.TotalSeconds;
            var heap = GC.GetTotalMemory(forceFullCollection: false);

            rows.Append(CultureInfo.InvariantCulture,
                $"{total.Elapsed.TotalSeconds:F1},{operations},{perSecond:F0},{heap},{System.Environment.WorkingSet},");
            rows.Append(CultureInfo.InvariantCulture, $"{GC.CollectionCount(0)},{GC.CollectionCount(1)},{GC.CollectionCount(2)}\n");

            Console.WriteLine(
                $"  {total.Elapsed.TotalSeconds,6:F0} s   {perSecond,12:N0} scopes/s   heap {heap / 1024 / 1024,5} MB   " +
                $"gen2 {GC.CollectionCount(2)}");

            windowOperations = 0;
            window.Restart();
        }

        var destination = outputDirectory ?? Paths.Artifacts;
        Directory.CreateDirectory(destination);
        var output = Path.Combine(destination, "soak.csv");
        File.WriteAllText(output, rows.ToString(), Encoding.UTF8);

        Console.WriteLine($"  {counter:N0} effects ran. Written to {output}");
        return 0;
    }
}
