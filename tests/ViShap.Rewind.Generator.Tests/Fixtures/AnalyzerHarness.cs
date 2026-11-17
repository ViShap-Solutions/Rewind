using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace ViShap.Rewind.Generator.Tests.Fixtures;

/// <summary>Runs one analyzer over consumer source, and applies a code fix to what it reports.</summary>
public static class AnalyzerHarness
{
    /// <summary>Returns what <paramref name="analyzer"/> reports on <paramref name="source"/>, in source order.</summary>
    public static ImmutableArray<Diagnostic> Diagnostics(DiagnosticAnalyzer analyzer, string source)
    {
        var run = GeneratorHarness.Run(source);
        return [.. run.Compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult()
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)];
    }

    /// <summary>Returns the source text each diagnostic points at.</summary>
    public static string[] Texts(IEnumerable<Diagnostic> diagnostics) =>
        [.. diagnostics.Select(diagnostic => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan))];

    /// <summary>
    /// Applies <paramref name="fix"/> to the single diagnostic <paramref name="analyzer"/> reports on <paramref name="source"/>,
    /// and returns the fixed source, or <see langword="null"/> when the fix offers no action.
    /// </summary>
    public static string? Fix(DiagnosticAnalyzer analyzer, CodeFixProvider fix, string source)
    {
        using var workspace = new AdhocWorkspace();
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var project = workspace.CurrentSolution
            .AddProject("Consumer", "Consumer", LanguageNames.CSharp)
            .WithParseOptions(parseOptions)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable))
            .AddMetadataReferences(GeneratorHarness.Compilation().References);
        var document = project.AddDocument("Source0.cs", SourceText.From(source));

        var compilation = document.Project.GetCompilationAsync().GetAwaiter().GetResult()!;
        var diagnostic = Assert.Single(compilation.WithAnalyzers([analyzer]).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult());

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        fix.RegisterCodeFixesAsync(context).GetAwaiter().GetResult();
        if (actions.Count == 0)
        {
            return null;
        }

        var operation = Assert.Single(actions.Single().GetOperationsAsync(CancellationToken.None).GetAwaiter().GetResult().OfType<ApplyChangesOperation>());
        return operation.ChangedSolution.GetDocument(document.Id)!.GetTextAsync().GetAwaiter().GetResult().ToString();
    }
}
