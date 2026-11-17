using ViShap.Rewind.Generator.Analyzers;
using ViShap.Rewind.Generator.CodeFixes;
using ViShap.Rewind.Generator.Tests.Fixtures;

namespace ViShap.Rewind.Generator.Tests.Analyzers;

public sealed class ScopeLeakTests
{
    [Fact]
    public void RWD1001_DiscardedOrNeverDisposed_ReportedAtBegin()
    {
        var diagnostics = AnalyzerHarness.Diagnostics(new ScopeLeakAnalyzer(), """
            using ViShap.Rewind;

            public static class Shop
            {
                public static void Discarded()
                {
                    Rewind.Begin();
                }

                public static void NeverDisposed(RewindCell<int> cell)
                {
                    var scope = Rewind.Begin();
                    cell.Value = 1;
                    scope.Commit();
                }
            }
            """);

        Assert.Equal(new[] { "RWD1001", "RWD1001" }, diagnostics.Select(d => d.Id));
        Assert.Equal(new[] { "Rewind.Begin()", "Rewind.Begin()" }, AnalyzerHarness.Texts(diagnostics));
        Assert.All(diagnostics, d => Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, d.Severity));
    }

    [Fact]
    public void RWD1001_DisposedOrHandedOver_NotReported()
    {
        var diagnostics = AnalyzerHarness.Diagnostics(new ScopeLeakAnalyzer(), """
            using System.Collections.Generic;
            using ViShap.Rewind;

            public sealed class Shop
            {
                private RewindScope? _held;

                public void UsingDeclaration(RewindCell<int> cell)
                {
                    using var scope = Rewind.Begin();
                    cell.Value = 1;
                }

                public void UsingStatement(RewindCell<int> cell)
                {
                    using (Rewind.Begin())
                    {
                        cell.Value = 1;
                    }

                    using (var scope = Rewind.Begin())
                    {
                        scope.Commit();
                    }
                }

                public void ExplicitDispose()
                {
                    var scope = Rewind.Begin();
                    try
                    {
                        scope.Commit();
                    }
                    finally
                    {
                        scope.Dispose();
                    }
                }

                public void ConditionalDispose()
                {
                    var scope = Rewind.Begin();
                    scope?.Dispose();
                }

                public RewindScope Returned() => Rewind.Begin();

                public RewindScope ReturnedLocal()
                {
                    var scope = Rewind.Begin();
                    return scope;
                }

                public void Stored() => _held = Rewind.Begin();

                public void Passed(List<RewindScope> scopes)
                {
                    var scope = Rewind.Begin();
                    scopes.Add(scope);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void RWD1001_Fix_DeclaresScopeWithUsing()
    {
        var fixedSource = AnalyzerHarness.Fix(new ScopeLeakAnalyzer(), new UseUsingCodeFix(), """
            using ViShap.Rewind;

            public static class Shop
            {
                public static void Pay(RewindCell<int> cell)
                {
                    // Opens the scope.
                    var scope = Rewind.Begin();
                    cell.Value = 1;
                    scope.Commit();
                }
            }
            """);

        Assert.Equal("""
            using ViShap.Rewind;

            public static class Shop
            {
                public static void Pay(RewindCell<int> cell)
                {
                    // Opens the scope.
                    using var scope = Rewind.Begin();
                    cell.Value = 1;
                    scope.Commit();
                }
            }
            """, fixedSource);
        Assert.Empty(GeneratorHarness.Run(fixedSource!).CompilationProblems);
        Assert.Empty(AnalyzerHarness.Diagnostics(new ScopeLeakAnalyzer(), fixedSource!));
    }

    [Fact]
    public void RWD1001_Fix_NotOfferedForDiscardedResult()
    {
        var fixedSource = AnalyzerHarness.Fix(new ScopeLeakAnalyzer(), new UseUsingCodeFix(), """
            using ViShap.Rewind;

            public static class Shop
            {
                public static void Leak() => Rewind.Begin();
            }
            """);

        Assert.Null(fixedSource);
    }
}
