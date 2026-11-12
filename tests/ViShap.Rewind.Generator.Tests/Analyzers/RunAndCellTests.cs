using Microsoft.CodeAnalysis;
using ViShap.Rewind.Generator.Analyzers;
using ViShap.Rewind.Generator.Tests.Fixtures;

namespace ViShap.Rewind.Generator.Tests.Analyzers;

public sealed class RunAndCellTests
{
    [Fact]
    public void RWD1003_AsynchronousBlocks_Reported()
    {
        const string source = """
            using System.Threading.Tasks;
            using ViShap.Rewind;

            public static class Shop
            {
                public static void Run()
                {
                    Rewind.Run(async () => await Task.Yield());
                    Rewind.Run(async scope => await Task.Yield());
                    Rewind.Run(async delegate () { await Task.Yield(); });
                    Rewind.Run(LaterAsync);
                    _ = Rewind.Run(async () => { await Task.Yield(); return 1; });
                }

                private static async void LaterAsync() => await Task.Yield();
            }
            """;

        var diagnostics = AnalyzerHarness.Diagnostics(new AsyncRunAnalyzer(), source);

        Assert.DoesNotContain(GeneratorHarness.Run(source).CompilationProblems, problem => problem.Severity == DiagnosticSeverity.Error);
        Assert.Equal(5, diagnostics.Length);
        Assert.All(diagnostics, d =>
        {
            Assert.Equal("RWD1003", d.Id);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        });
        Assert.Equal("LaterAsync", AnalyzerHarness.Texts(diagnostics)[3]);
    }

    [Fact]
    public void RWD1003_SynchronousBlocks_NotReported()
    {
        var diagnostics = AnalyzerHarness.Diagnostics(new AsyncRunAnalyzer(), """
            using ViShap.Rewind;

            public static class Shop
            {
                public static void Run(RewindCell<int> cell)
                {
                    Rewind.Run(() => cell.Value = 1);
                    Rewind.Run(scope => scope.AfterCommit(() => { }));
                    Rewind.Run(cell, static c => c.Value = 2);
                    Rewind.Run(Now);
                    _ = Rewind.Run(() => cell.Value);
                }

                private static void Now()
                {
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void RWD1003_AsynchronousEffects_Reported()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using ViShap.Rewind;

            public static class Shop
            {
                public static Task Run() => Rewind.RunAsync(async scope =>
                {
                    await Task.Yield();
                    scope.AfterCommit(async () => await Task.Yield());
                    scope.AfterRollback(LaterAsync);
                    scope.Effect(async () => await Task.Yield(), () => { });
                    scope.Effect(() => { }, async delegate () { await Task.Yield(); });
                });

                private static async void LaterAsync() => await Task.Yield();
            }
            """;

        var diagnostics = AnalyzerHarness.Diagnostics(new AsyncRunAnalyzer(), source);

        Assert.DoesNotContain(GeneratorHarness.Run(source).CompilationProblems, problem => problem.Severity == DiagnosticSeverity.Error);
        Assert.Equal(4, diagnostics.Length);
        Assert.All(diagnostics, d =>
        {
            Assert.Equal("RWD1003", d.Id);
            Assert.Equal(DiagnosticSeverity.Error, d.Severity);
            Assert.Contains("async void", d.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        });
        Assert.Equal("LaterAsync", AnalyzerHarness.Texts(diagnostics)[1]);
    }

    [Fact]
    public void RWD1003_SynchronousEffectsAndRunAsync_NotReported()
    {
        var diagnostics = AnalyzerHarness.Diagnostics(new AsyncRunAnalyzer(), """
            using System.Threading.Tasks;
            using ViShap.Rewind;

            public static class Shop
            {
                public static async Task Run(RewindCell<int> cell)
                {
                    await Rewind.RunAsync(async () => await Task.Yield());
                    await Rewind.RunAsync(async scope =>
                    {
                        await Task.Yield();
                        scope.AfterCommit(() => cell.Value = 1);
                        scope.Effect(() => { }, Undo);
                    });
                    _ = await Rewind.RunAsync(async () => { await Task.Yield(); return 1; });
                }

                private static void Undo()
                {
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void RWD1004_MutableReferenceCell_ReportedAtCreation()
    {
        var diagnostics = AnalyzerHarness.Diagnostics(new MutableCellAnalyzer(), """
            using System.Collections.Generic;
            using ViShap.Rewind;

            public sealed class Address { public string City { get; set; } = ""; }

            public sealed class Shop
            {
                private readonly RewindCell<List<int>> _lines = new(new List<int>());
                private readonly RewindCell<Address> _address = new RewindCell<Address>(new Address());
                private readonly RewindCell<int[]> _codes = new();
            }
            """);

        Assert.Equal(new[] { "new(new List<int>())", "new RewindCell<Address>(new Address())", "new()" }, AnalyzerHarness.Texts(diagnostics));
        Assert.All(diagnostics, d => Assert.Equal("RWD1004", d.Id));
        Assert.Contains("List<int>", diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void RWD1004_ImmutableOrValueCell_NotReported()
    {
        var diagnostics = AnalyzerHarness.Diagnostics(new MutableCellAnalyzer(), """
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using ViShap.Rewind;

            public sealed record Money(decimal Amount);

            public sealed class Shop
            {
                private readonly RewindCell<int> _count = new();
                private readonly RewindCell<string> _name = new("a");
                private readonly RewindCell<Money> _price = new(new Money(1m));
                private readonly RewindCell<ImmutableList<int>> _lines = new(ImmutableList<int>.Empty);
                private readonly RewindCell<IReadOnlyList<int>> _view = new([]);
            }
            """);

        Assert.Empty(diagnostics);
    }
}
