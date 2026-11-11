using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ViShap.Rewind.Generator.Tests.Fixtures;

namespace ViShap.Rewind.Generator.Tests.Pipeline;

public sealed class CachingTests
{
    private const string Rewindable = """
        using ViShap.Rewind;

        namespace Shop;

        [Rewindable]
        public partial class Order
        {
            public partial decimal Total { get; set; }
        }
        """;

    [Fact]
    public void Run_UnrelatedEditAfterFirstRun_IsServedFromCache()
    {
        var compilation = GeneratorHarness.Compilation(Rewindable, "namespace Shop; public static class Helper { public static int One() => 1; }");
        var first = GeneratorHarness.Run(GeneratorHarness.Driver(), compilation);
        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Last(),
            CSharpSyntaxTree.ParseText("namespace Shop; public static class Helper { public static int Two() => 2; }", (CSharpParseOptions)compilation.SyntaxTrees.First().Options, path: "Source1.cs"));

        var second = first.Driver.RunGenerators(edited).GetRunResult().Results.Single();

        var reasons = second.TrackedSteps[RewindGenerator.PlanStep]
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)
            .ToList();
        Assert.NotEmpty(reasons);
        Assert.All(reasons, reason => Assert.True(reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, reason.ToString()));
        Assert.All(second.TrackedOutputSteps.SelectMany(step => step.Value).SelectMany(step => step.Outputs), output =>
            Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, output.Reason.ToString()));
    }

    [Fact]
    public void Run_EditToRewindableType_RegeneratesIt()
    {
        var compilation = GeneratorHarness.Compilation(Rewindable);
        var first = GeneratorHarness.Run(GeneratorHarness.Driver(), compilation);
        var edited = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText(Rewindable.Replace("decimal Total", "int Total", StringComparison.Ordinal), (CSharpParseOptions)compilation.SyntaxTrees.First().Options, path: "Source0.cs"));

        var second = GeneratorHarness.Run(first.Driver, edited);

        Assert.Contains("public partial int Total", second.Sources["Shop.Order.Rewind.g.cs"]);
        Assert.Empty(second.CompilationProblems);
    }

    [Fact]
    public void Run_TwiceOverSameCompilation_ProducesByteIdenticalSources()
    {
        var first = GeneratorHarness.Run(Rewindable);

        var second = GeneratorHarness.Run(Rewindable);

        Assert.Equal(first.Sources, second.Sources);
        Assert.All(first.Sources.Values, source => Assert.DoesNotContain("\r", source));
    }

    [Fact]
    public void PipelineModels_HoldOnlyValues()
    {
        var modelTypes = typeof(RewindGenerator).Assembly.GetTypes()
            .Where(type => type.Namespace == "ViShap.Rewind.Generator.Model" && !type.IsGenericTypeDefinition)
            .ToList();
        var forbidden = new[] { typeof(ISymbol), typeof(SyntaxNode), typeof(Compilation), typeof(Location), typeof(SemanticModel) };

        Assert.NotEmpty(modelTypes);
        foreach (var type in modelTypes)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.False(forbidden.Any(candidate => candidate.IsAssignableFrom(field.FieldType)), $"{type.Name}.{field.Name} holds {field.FieldType.Name}");
            }
        }
    }
}
