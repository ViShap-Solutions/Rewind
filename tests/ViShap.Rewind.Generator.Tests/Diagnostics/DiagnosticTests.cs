using Microsoft.CodeAnalysis;
using ViShap.Rewind.Generator.Tests.Fixtures;

namespace ViShap.Rewind.Generator.Tests.Diagnostics;

public sealed class DiagnosticTests
{
    [Fact]
    public void RWD0001_TypeNotPartial_ReportedAtTypeNameAndNothingGenerated()
    {
        var run = GeneratorHarness.Run("""
            using ViShap.Rewind;

            [Rewindable]
            public class Order
            {
                public decimal Total { get; set; }
            }
            """);

        var diagnostic = Assert.Single(run.AnalyzerDiagnostics);
        Assert.Equal("RWD0001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Order", Text(diagnostic));
        Assert.Empty(run.Sources);
    }

    [Fact]
    public void RWD0001_ContainingTypeNotPartial_ReportedAtContainingTypeName()
    {
        var run = GeneratorHarness.Run("""
            using ViShap.Rewind;

            public class Outer
            {
                [Rewindable]
                public partial class Inner
                {
                    public partial int Value { get; set; }
                }
            }
            """);

        var diagnostic = Assert.Single(run.AnalyzerDiagnostics, d => d.Id == "RWD0001");
        Assert.Equal("Outer", Text(diagnostic));
        Assert.Empty(run.Sources);
    }

    [Fact]
    public void RWD0002_WritableAutoProperty_ReportedAtPropertyName()
    {
        var run = GeneratorHarness.Run("""
            using ViShap.Rewind;

            [Rewindable]
            public partial class Order
            {
                public partial decimal Total { get; set; }
                public string Note { get; set; } = "";
                public static int Created { get; set; }
            }
            """);

        var diagnostics = run.AnalyzerDiagnostics.Where(d => d.Id == "RWD0002").ToList();
        Assert.Equal(new[] { "Note", "Created" }, diagnostics.Select(Text));
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }

    [Fact]
    public void RWD0002_PropertiesThatAreFineOrDeliberate_NotReported()
    {
        var run = GeneratorHarness.Run("""
            using ViShap.Rewind;

            [Rewindable]
            public partial record Order(int Id)
            {
                private readonly RewindCell<int> _count = new();

                public partial decimal Total { get; set; }
                public string Code { get; } = "";
                public string Reference { get; init; } = "";
                public RewindList<string> Lines { get; set; } = new();
                [RewindIgnore] public int Views { get; set; }
                public int Count { get => _count.Value; set => _count.Value = value; }
            }
            """);

        Assert.DoesNotContain(run.AnalyzerDiagnostics, d => d.Id == "RWD0002");
        Assert.Empty(run.CompilationProblems);
    }

    [Fact]
    public void RWD0003_InitAccessor_ReportedAtPropertyName()
    {
        var run = GeneratorHarness.Run("""
            using ViShap.Rewind;

            [Rewindable]
            public partial class Order
            {
                public partial decimal Total { get; init; }
            }
            """);

        var diagnostic = Assert.Single(run.AnalyzerDiagnostics);
        Assert.Equal("RWD0003", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Total", Text(diagnostic));
    }

    [Fact]
    public void RWD0003_RefReturn_Reported()
    {
        var run = GeneratorHarness.Run("""
            using ViShap.Rewind;

            [Rewindable]
            public partial class Order
            {
                public partial ref int Total { get; }
            }
            """);

        Assert.Contains(run.AnalyzerDiagnostics, d => d.Id is "RWD0003" or "RWD0005");
        Assert.Equal("Total", Text(Assert.Single(run.AnalyzerDiagnostics)));
    }

    [Fact]
    public void RWD0004_MutableReferenceTypes_ReportedAtPropertyName()
    {
        var run = GeneratorHarness.Run("""
            using System.Collections.Generic;
            using System.Text;
            using ViShap.Rewind;

            public sealed class Address { public string City { get; set; } = ""; }
            public sealed class Bag { public int Size; }

            [Rewindable]
            public partial class Order
            {
                public partial List<string> Lines { get; set; }
                public partial int[] Codes { get; set; }
                public partial Address Shipping { get; set; }
                public partial Bag Bag { get; set; }
                public partial StringBuilder Text { get; set; }
                public partial IList<int> Numbers { get; set; }
                public partial HashSet<int> Seen { get; set; }
            }
            """);

        var diagnostics = run.AnalyzerDiagnostics.Where(d => d.Id == "RWD0004").ToList();
        Assert.Equal(new[] { "Lines", "Codes", "Shipping", "Bag", "Text", "Numbers", "Seen" }, diagnostics.Select(Text));
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
        Assert.Contains("Address", diagnostics[2].GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Single(run.Sources);
    }

    [Fact]
    public void RWD0004_ImmutableOrTrackedTypes_NotReported()
    {
        var run = GeneratorHarness.Run("""
            using System;
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Collections.ObjectModel;
            using ViShap.Rewind;

            public sealed record Money(decimal Amount, string Currency);

            [Rewindable]
            public partial class Customer
            {
                public partial string Name { get; set; }
            }

            [Rewindable]
            public partial class Order
            {
                public partial string Code { get; set; }
                public partial string? Note { get; set; }
                public partial object? Tag { get; set; }
                public partial Action? Callback { get; set; }
                public partial Money Price { get; set; }
                public partial ImmutableList<string> Lines { get; set; }
                public partial IReadOnlyList<int> Numbers { get; set; }
                public partial IEnumerable<int> Stream { get; set; }
                public partial ReadOnlyCollection<int> Fixed { get; set; }
                public partial RewindList<int> Tracked { get; set; }
                public partial Customer Buyer { get; set; }
                public partial Uri Link { get; set; }
                [RewindIgnore] public partial List<int> Cache { get; set; }
            }
            """);

        Assert.DoesNotContain(run.AnalyzerDiagnostics, d => d.Id == "RWD0004");
        Assert.Empty(run.CompilationProblems);
    }

    [Fact]
    public void RWD0005_StaticIndexerOrGetOnly_ReportedAtDeclaration()
    {
        var run = GeneratorHarness.Run("""
            using ViShap.Rewind;

            [Rewindable]
            public partial class Order
            {
                public static partial int Created { get; set; }
                public partial int this[int index] { get; set; }
                public partial int Total { get; }
            }
            """);

        var diagnostics = run.AnalyzerDiagnostics.Where(d => d.Id == "RWD0005").ToList();
        Assert.Equal(3, diagnostics.Count);
        Assert.Equal(new[] { "Created", "this", "Total" }, diagnostics.Select(Text));
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void UserImplementedPartialProperty_IsLeftAlone()
    {
        var run = GeneratorHarness.Run("""
            using ViShap.Rewind;

            [Rewindable]
            public partial class Order
            {
                private int _total;
                public partial int Total { get; set; }
                public partial int Total { get => _total; set => _total = value; }
            }
            """);

        Assert.Empty(run.AnalyzerDiagnostics);
        Assert.Empty(run.Sources);
        Assert.Empty(run.CompilationProblems);
    }

    [Fact]
    public void AnalyzerReleases_ListEveryDiagnostic()
    {
        var unshipped = File.ReadAllText(Path.Combine(ProjectRoot(), "src", "ViShap.Rewind.Generator", "AnalyzerReleases.Unshipped.md"));

        foreach (var (id, severity) in new[]
        {
            ("RWD0001", "Error"), ("RWD0002", "Warning"), ("RWD0003", "Error"), ("RWD0004", "Warning"), ("RWD0005", "Error"),
            ("RWD1001", "Warning"), ("RWD1002", "Warning"), ("RWD1003", "Error"), ("RWD1004", "Warning"), ("RWD1005", "Warning"),
        })
        {
            Assert.Matches($@"(?m)^{id}\s*\|\s*ViShap\.Rewind\s*\|\s*{severity}\s*\|", unshipped);
        }
    }

    [Fact]
    public void EveryDiagnostic_HasHelpLinkSectionAndReadmeRow()
    {
        var page = File.ReadAllText(Path.Combine(ProjectRoot(), "docs", "diagnostics.md"));
        var readme = File.ReadAllText(Path.Combine(ProjectRoot(), "src", "ViShap.Rewind.Generator", "GENERATOR-README.md"));

        var descriptors = typeof(RewindableAnalyzer).Assembly.GetTypes()
            .Where(type => typeof(Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => ((Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer)Activator.CreateInstance(type)!).SupportedDiagnostics)
            .ToList();

        Assert.Equal(10, descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            Assert.Equal($"https://github.com/ViShap-Solutions/Rewind/blob/main/docs/diagnostics.md#{descriptor.Id.ToLowerInvariant()}", descriptor.HelpLinkUri);
            Assert.Matches($@"(?m)^## {descriptor.Id}\r?$", page);
            Assert.Matches($@"(?m)^\| \[{descriptor.Id}\]\(#{descriptor.Id.ToLowerInvariant()}\) \| {descriptor.DefaultSeverity} \|", page);
            Assert.Matches($@"(?m)^\| {descriptor.Id} \| {descriptor.DefaultSeverity} \|", readme);
        }
    }

    private static string Text(Diagnostic diagnostic) =>
        diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string ProjectRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rewind.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found.");
    }
}
