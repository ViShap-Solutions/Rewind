using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ViShap.Rewind.Generator.Tests;

public sealed class PipelineTests
{
    [Fact]
    public void Run_CompilationWithoutRewindableTypes_ProducesNoSourceAndNoDiagnostic()
    {
        var compilation = CSharpCompilation.Create(
            "Consumer",
            [CSharpSyntaxTree.ParseText("public sealed class Plain { public int Value { get; set; } }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new RewindGenerator()).RunGenerators(compilation);
        var result = driver.GetRunResult();

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedTrees);
    }
}
