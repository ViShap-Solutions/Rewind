namespace ViShap.Rewind.Tests.Fixtures;

public sealed class ApiFixtureTests
{
    [Fact]
    public void TypeHeader_SealedClassWithInterface_RendersKindNameAndInterface()
    {
        var header = PublicApi.TypeHeader(typeof(RewindScope));

        Assert.Equal("sealed class RewindScope : IDisposable", header);
    }

    [Fact]
    public void TypeHeader_NestedStructOfGenericType_RendersQualifiedName()
    {
        var header = PublicApi.TypeHeader(typeof(RewindList<>.Enumerator));

        Assert.Equal("struct RewindList<T>.Enumerator : IEnumerator<T>", header);
    }

    [Fact]
    public void Members_IndexerAndNestedReturnType_RenderInListingNotation()
    {
        var members = PublicApi.Members(typeof(RewindList<>)).ToList();

        Assert.Contains("T this[int index] { get; set; }", members);
        Assert.Contains("RewindList<T>.Enumerator GetEnumerator()", members);
    }

    [Fact]
    public void DocumentationIds_Indexer_IncludesParameters()
    {
        var indexer = typeof(RewindDictionary<,>).GetProperty("Item")!;

        Assert.Equal("P:ViShap.Rewind.RewindDictionary`2.Item(`0)", DocumentationIds.Of(indexer));
    }

    [Fact]
    public void Members_SampleType_RendersEveryVisibleMemberInListingNotation()
    {
        var members = PublicApi.Members(typeof(Sample<>)).ToList();

        Assert.Equal(
            new[]
            {
                "Sample(string? name, ref int count)",
                "protected Sample()",
                "static int? Count { get; }",
                "IReadOnlyList<string?> Names { get; set; }",
                "TResult Map<TResult>(Func<T, TResult> map, params int[] values) where TResult : class, IDisposable",
                "protected void Reset()",
            },
            members);
    }

    [Fact]
    public void DocumentationIds_CoreMembers_MatchCompilerIdentifiers()
    {
        var run = typeof(Rewind).GetMethods().Single(method =>
            method.Name == nameof(Rewind.Run) && method.IsGenericMethodDefinition
            && method.GetParameters() is [{ ParameterType.IsGenericParameter: true }]);
        var effect = typeof(RewindScope).GetMethod(nameof(RewindScope.Effect))!;
        var value = typeof(RewindCell<>).GetProperty("Value")!;
        var constructor = typeof(RewindCompensationException).GetConstructors().Single();

        Assert.Equal("T:ViShap.Rewind.RewindCell`1", DocumentationIds.Of(typeof(RewindCell<>)));
        Assert.Equal("M:ViShap.Rewind.Rewind.Run``1(``0)", DocumentationIds.Of(run));
        Assert.Equal("M:ViShap.Rewind.RewindScope.Effect(System.Action,System.Action)", DocumentationIds.Of(effect));
        Assert.Equal("P:ViShap.Rewind.RewindCell`1.Value", DocumentationIds.Of(value));
        Assert.Equal(
            "M:ViShap.Rewind.RewindCompensationException.#ctor(System.Exception,System.Collections.Generic.IReadOnlyList{System.Exception})",
            DocumentationIds.Of(constructor));
    }

    [Fact]
    public void Extract_MarkdownWithBlocks_ReturnsCsharpBlocksWithFenceLines()
    {
        const string markdown = "# Title\n\n```csharp\nvar a = 1;\n```\n\n```text\nnot code\n```\n\n```csharp\nvar b = 2;\n```\n";

        var examples = CodeExamples.Extract("page.md", markdown).ToList();

        Assert.Equal(new[] { "page.md:3", "page.md:11" }, examples.Select(example => example.Location));
        Assert.Equal("var a = 1;\n", examples[0].Code);
    }

    [Fact]
    public void Compile_BrokenExample_ReportsProblemAtPageAndLine()
    {
        var example = new CodeExample("page.md", 7, "using ViShap.Rewind;\n\nRewind.Run(() => missing.Value = 1);\n");

        var (assembly, problems) = CodeExamples.Compile(example);

        Assert.Null(assembly);
        Assert.Contains("page.md:10: CS0103:", problems.Select(example.Describe).Single(description => description.Contains("CS0103")));
    }

    [Fact]
    public void Compile_ExampleWithWarning_ReportsWarning()
    {
        var example = new CodeExample("page.md", 1, "string text = null;\nConsole.WriteLine(text);\n");

        var (assembly, problems) = CodeExamples.Compile(example);

        Assert.Null(assembly);
        Assert.Contains(problems, problem => problem.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Run_ExampleThrows_ReturnsException()
    {
        var example = new CodeExample("page.md", 1, "throw new InvalidOperationException(\"example failed\");\n");
        var (assembly, _) = CodeExamples.Compile(example);

        var exception = CodeExamples.Run(assembly!);

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void Run_ExampleUsingCore_Completes()
    {
        var example = new CodeExample("page.md", 1, "using ViShap.Rewind;\n\nvar cell = new RewindCell<int>(1);\nRewind.Run(() => cell.Value = 2);\nif (cell.Value != 2) throw new InvalidOperationException();\n");
        var (assembly, problems) = CodeExamples.Compile(example);

        var exception = CodeExamples.Run(assembly!);

        Assert.Empty(problems);
        Assert.Null(exception);
    }

    [Fact]
    public void Root_HoldsSolutionFile()
    {
        Assert.True(File.Exists(Repository.PathOf("Rewind.slnx")));
    }

    public class Sample<T>
    {
        public Sample(string? name, ref int count)
        {
            Names = [name];
            count++;
        }

        protected Sample()
        {
            Names = [];
        }

        public static int? Count => null;

        public IReadOnlyList<string?> Names { get; set; }

        public TResult Map<TResult>(Func<T, TResult> map, params int[] values)
            where TResult : class, IDisposable => throw new NotSupportedException();

        protected void Reset()
        {
        }

        internal void Hidden()
        {
        }
    }
}
