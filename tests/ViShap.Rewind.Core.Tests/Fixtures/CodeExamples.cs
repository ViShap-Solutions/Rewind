using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using ViShap.Rewind.Generator;

namespace ViShap.Rewind.Tests.Fixtures;

/// <summary>One <c>csharp</c> block of a documentation page, with the page and the line of its opening fence.</summary>
public sealed record CodeExample(string Page, int Line, string Code)
{
    /// <summary>Gets where the block is, as <c>page:line</c>.</summary>
    public string Location => $"{Page}:{Line}";

    /// <summary>Describes <paramref name="diagnostic"/> at the line of the page it points to, as <c>page:line: id: message</c>.</summary>
    public string Describe(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var line = span.Path == Page ? Line + span.StartLinePosition.Line + 1 : Line;
        return $"{Page}:{line}: {diagnostic.Id}: {diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}";
    }
}

/// <summary>Finds the <c>csharp</c> blocks of the consumer documentation, compiles them as a consumer would, and runs them.</summary>
public static partial class CodeExamples
{
    private static readonly string[] s_implicitUsings =
    [
        "System",
        "System.Collections.Generic",
        "System.IO",
        "System.Linq",
        "System.Net.Http",
        "System.Threading",
        "System.Threading.Tasks",
    ];

    private static readonly Lazy<MetadataReference[]> s_references = new(LoadReferences);

    private static readonly Lazy<ImmutableArray<DiagnosticAnalyzer>> s_analyzers = new(() =>
        [.. typeof(RewindGenerator).Assembly.GetTypes()
            .Where(type => typeof(DiagnosticAnalyzer).IsAssignableFrom(type) && !type.IsAbstract)
            .Select(type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!)]);

    /// <summary>Returns the documentation pages: every page of <c>docs/</c>, the repository README and every package README.</summary>
    public static IEnumerable<string> Pages()
    {
        var docs = Directory.EnumerateFiles(Repository.PathOf("docs"), "*.md", SearchOption.AllDirectories);
        var packageReadmes = Directory.EnumerateFiles(Repository.PathOf("src"), "*-README.md", SearchOption.AllDirectories);
        return docs.Concat(packageReadmes).Append(Repository.PathOf("README.md"))
            .Select(Repository.Relative)
            .OrderBy(page => page, StringComparer.Ordinal);
    }

    /// <summary>Returns every <c>csharp</c> block of every documentation page.</summary>
    public static IEnumerable<CodeExample> All() => Pages().SelectMany(page => Extract(page, File.ReadAllText(Repository.PathOf(page))));

    /// <summary>Returns the <c>csharp</c> blocks of <paramref name="markdown"/>, attributed to <paramref name="page"/>.</summary>
    public static IEnumerable<CodeExample> Extract(string page, string markdown)
    {
        var text = markdown.Replace("\r\n", "\n");
        foreach (Match match in Fence().Matches(text))
        {
            var line = text.AsSpan(0, match.Index).Count('\n') + 1;
            yield return new CodeExample(page, line, match.Groups["code"].Value);
        }
    }

    /// <summary>
    /// Compiles <paramref name="example"/> as a consumer project of the meta-package would: implicit usings, nullable
    /// reference types, the latest language version, the source generator and every analyzer of its package. Returns the assembly, or the
    /// errors and warnings that prevented it.
    /// </summary>
    public static (byte[]? Assembly, IReadOnlyList<Diagnostic> Problems) Compile(CodeExample example)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var code = CSharpSyntaxTree.ParseText(example.Code, parseOptions, path: example.Page);
        var usings = CSharpSyntaxTree.ParseText(
            string.Concat(s_implicitUsings.Select(ns => $"global using global::{ns};\n")),
            parseOptions,
            path: "ImplicitUsings.cs");
        var topLevel = code.GetRoot().ChildNodes().OfType<GlobalStatementSyntax>().Any();
        var options = new CSharpCompilationOptions(topLevel ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary)
            .WithNullableContextOptions(NullableContextOptions.Enable);
        var compilation = CSharpCompilation.Create("Example", [usings, code], s_references.Value, options);

        CSharpGeneratorDriver.Create(new RewindGenerator())
            .WithUpdatedParseOptions(parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var generatorDiagnostics);

        using var output = new MemoryStream();
        var result = generated.Emit(output);
        var analyzerDiagnostics = generated.WithAnalyzers(s_analyzers.Value).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
        var problems = generatorDiagnostics.Concat(result.Diagnostics).Concat(analyzerDiagnostics)
            .Where(diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .ToList();

        return (result.Success && problems.Count == 0 ? output.ToArray() : null, problems);
    }

    /// <summary>
    /// Runs the entry point of <paramref name="assembly"/> on a dedicated thread with the console output discarded.
    /// Returns the exception it ended with, or <see langword="null"/>.
    /// </summary>
    public static Exception? Run(byte[] assembly)
    {
        var context = new AssemblyLoadContext("Example", isCollectible: true);
        var previous = Console.Out;
        Console.SetOut(TextWriter.Null);
        try
        {
            var entryPoint = context.LoadFromStream(new MemoryStream(assembly)).EntryPoint
                ?? throw new InvalidOperationException("The example has no entry point.");
            return OtherThread.Capture(() =>
            {
                try
                {
                    var arguments = entryPoint.GetParameters().Length == 0 ? null : new object[] { Array.Empty<string>() };
                    if (entryPoint.Invoke(null, arguments) is Task task)
                    {
                        task.GetAwaiter().GetResult();
                    }
                }
                catch (TargetInvocationException exception) when (exception.InnerException is not null)
                {
                    throw exception.InnerException;
                }
            });
        }
        finally
        {
            Console.SetOut(previous);
            context.Unload();
        }
    }

    private static MetadataReference[] LoadReferences()
    {
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return platform
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal)
                || Path.GetFileName(path) is "mscorlib.dll" or "netstandard.dll" or "Microsoft.Win32.Primitives.dll")
            .Append(typeof(Rewind).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    [GeneratedRegex(@"^```csharp\n(?<code>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex Fence();
}
