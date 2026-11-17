using Microsoft.CodeAnalysis;
using ViShap.Rewind.Generator.Analyzers;
using ViShap.Rewind.Generator.Tests.Fixtures;

namespace ViShap.Rewind.Generator.Tests.Analyzers;

public sealed class ParallelBranchTests
{
    [Fact]
    public void RWD1006_ParallelBranchesInAsynchronousScope_Reported()
    {
        const string source = """
            using System.Threading.Tasks;
            using ViShap.Rewind;

            public static class Shop
            {
                public static Task Run(RewindCell<int> cell, int[] items) => Rewind.RunAsync(async () =>
                {
                    await Task.WhenAll(Task.CompletedTask, Task.CompletedTask);
                    await Task.WhenAny(Task.CompletedTask);
                    await Task.Run(() => cell.Value = 1);
                    await Task.Factory.StartNew(() => cell.Value = 2);
                    Parallel.For(0, 2, i => cell.Value = i);
                    Parallel.ForEach(items, i => cell.Value = i);
                    await Parallel.ForEachAsync(items, (i, _) => ValueTask.CompletedTask);
                    Parallel.Invoke(() => cell.Value = 3);
                    Rewind.Run(() => Task.Run(() => { }).Wait());
                });
            }
            """;

        var diagnostics = AnalyzerHarness.Diagnostics(new ParallelBranchAnalyzer(), source);

        Assert.DoesNotContain(GeneratorHarness.Run(source).CompilationProblems, problem => problem.Severity == DiagnosticSeverity.Error);
        Assert.Equal(9, diagnostics.Length);
        Assert.All(diagnostics, d =>
        {
            Assert.Equal("RWD1006", d.Id);
            Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        });
        Assert.StartsWith("Task.WhenAll(", AnalyzerHarness.Texts(diagnostics)[0]);
        Assert.Contains("'Task.WhenAll'", diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void RWD1006_OutsideAsynchronousScope_NotReported()
    {
        var diagnostics = AnalyzerHarness.Diagnostics(new ParallelBranchAnalyzer(), """
            using System.Threading.Tasks;
            using ViShap.Rewind;

            public static class Shop
            {
                public static async Task Run(RewindCell<int> cell)
                {
                    await Task.WhenAll(
                        Rewind.RunAsync(async () => { await Task.Yield(); cell.Value = 1; }),
                        Rewind.RunAsync(async () => { await Task.Yield(); cell.Value = 2; }));
                    await Rewind.RunAsync(async scope =>
                    {
                        await Task.Yield();
                        scope.AfterCommit(() => Task.Run(() => { }));
                        Local();
                    });
                    Rewind.Run(() => Task.Run(() => { }).Wait());

                    static void Local() => Task.Run(() => { }).Wait();
                }
            }
            """);

        Assert.Empty(diagnostics);
    }
}
