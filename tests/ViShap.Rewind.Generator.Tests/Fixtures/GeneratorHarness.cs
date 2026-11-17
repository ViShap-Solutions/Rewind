using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ViShap.Rewind.Generator.Tests.Fixtures;

/// <summary>The outcome of running the generator over one consumer compilation.</summary>
/// <param name="Driver">The driver after the run, for a second run.</param>
/// <param name="Sources">The generated sources by hint name.</param>
/// <param name="GeneratorDiagnostics">What the generator reported.</param>
/// <param name="Compilation">The consumer compilation with the generated sources added.</param>
public sealed record GeneratorRun(
    GeneratorDriver Driver,
    ImmutableDictionary<string, string> Sources,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    Compilation Compilation)
{
    /// <summary>Gets the errors and warnings of compiling the consumer with the generated sources.</summary>
    public ImmutableArray<Diagnostic> CompilationProblems =>
        [.. Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)];

    /// <summary>Gets what <see cref="RewindableAnalyzer"/> reports on the consumer, in source order.</summary>
    public ImmutableArray<Diagnostic> AnalyzerDiagnostics =>
        [.. Compilation.WithAnalyzers([new RewindableAnalyzer()])
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult()
            .OrderBy(diagnostic => diagnostic.Location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.SourceSpan.Start)];
}

/// <summary>Compiles consumer source against Core, as a project referencing the meta-package would, and runs the generator.</summary>
public static class GeneratorHarness
{
    private static readonly CSharpParseOptions s_parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    private static readonly Lazy<MetadataReference[]> s_references = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal)
                || Path.GetFileName(path) is "mscorlib.dll" or "netstandard.dll")
            .Append(typeof(ViShap.Rewind.Rewind).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray());

    /// <summary>Returns a consumer compilation of <paramref name="sources"/>, with nullable reference types on.</summary>
    public static CSharpCompilation Compilation(params string[] sources) =>
        CSharpCompilation.Create(
            "Consumer",
            sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, s_parseOptions, path: $"Source{index}.cs")),
            s_references.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithNullableContextOptions(NullableContextOptions.Enable));

    /// <summary>Creates a driver that tracks the steps of the generator's pipeline.</summary>
    public static GeneratorDriver Driver() =>
        CSharpGeneratorDriver.Create(
            [new RewindGenerator().AsSourceGenerator()],
            parseOptions: s_parseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    /// <summary>Runs the generator over <paramref name="sources"/>.</summary>
    public static GeneratorRun Run(params string[] sources) => Run(Driver(), Compilation(sources));

    /// <summary>Runs <paramref name="driver"/> over <paramref name="compilation"/>.</summary>
    public static GeneratorRun Run(GeneratorDriver driver, Compilation compilation)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult().Results.Single();
        return new GeneratorRun(
            driver,
            result.GeneratedSources.ToImmutableDictionary(source => source.HintName, source => source.SourceText.ToString()),
            result.Diagnostics,
            output);
    }
}
