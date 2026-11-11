using ViShap.Rewind.Generator.Tests.Fixtures;

namespace ViShap.Rewind.Generator.Tests.Emission;

public sealed class EmissionTests
{
    [Fact]
    public void Emit_PlainClass_MatchesSnapshot() => Verify("PlainClass", "Shop.Order.Rewind.g.cs", """
        using ViShap.Rewind;

        namespace Shop;

        public enum OrderStatus { New, Paid }

        [Rewindable]
        public partial class Order
        {
            public partial decimal Total { get; set; }

            public partial OrderStatus Status { get; set; }

            public RewindList<string> Lines { get; } = new();
        }
        """);

    [Fact]
    public void Emit_RecordClass_MatchesSnapshot() => Verify("RecordClass", "Shop.Customer.Rewind.g.cs", """
        using ViShap.Rewind;

        namespace Shop;

        [Rewindable]
        public partial record Customer(int Id)
        {
            public partial string Name { get; set; }
        }
        """);

    [Fact]
    public void Emit_GenericTypeWithConstraint_MatchesSnapshot() => Verify("GenericType", "Shop.Box_1.Rewind.g.cs", """
        using ViShap.Rewind;

        namespace Shop;

        [Rewindable]
        public partial class Box<T>
            where T : notnull
        {
            public partial T Content { get; set; }

            public partial int Count { get; set; }
        }
        """);

    [Fact]
    public void Emit_NestedType_MatchesSnapshot() => Verify("NestedType", "Shop.Outer_1.Middle.Inner.Rewind.g.cs", """
        using ViShap.Rewind;

        namespace Shop;

        public partial class Outer<TKey>
        {
            public partial record Middle
            {
                [Rewindable]
                internal sealed partial class Inner
                {
                    public partial TKey? Key { get; set; }
                }
            }
        }
        """);

    [Fact]
    public void Emit_GlobalNamespace_MatchesSnapshot() => Verify("GlobalNamespace", "Counter.Rewind.g.cs", """
        using ViShap.Rewind;

        [Rewindable]
        public partial class Counter
        {
            public partial int Value { get; set; }
        }
        """);

    [Fact]
    public void Emit_ModifiersNullableAndIgnored_MatchesSnapshot() => Verify("Modifiers", "Shop.Account.Rewind.g.cs", """
        using ViShap.Rewind;

        namespace Shop;

        [Rewindable]
        public partial class Account
        {
            public partial decimal Balance { get; private set; }

            public virtual partial string? Note { get; set; }

            public required partial string Owner { get; set; }

            protected internal partial int Version { get; set; }

            [RewindIgnore]
            public partial int ViewCount { get; set; }

            public void Deposit(decimal amount) => Balance += amount;
        }
        """);

    [Fact]
    public void Emit_PropertiesSpreadOverParts_MatchesSnapshot() => Verify("SeveralParts", "Shop.Cart.Rewind.g.cs",
        """
        using ViShap.Rewind;

        namespace Shop;

        [Rewindable]
        public partial class Cart
        {
            public partial int Items { get; set; }
        }
        """,
        """
        namespace Shop;

        public partial class Cart
        {
            public partial decimal Total { get; set; }
        }
        """);

    [Fact]
    public void Emit_SeveralTypes_WritesOneFilePerType()
    {
        var run = GeneratorHarness.Run("""
            using ViShap.Rewind;

            namespace Shop;

            [Rewindable] public partial class A { public partial int X { get; set; } }
            [Rewindable] public partial class B { public partial int Y { get; set; } }
            """);

        Assert.Equal(new[] { "Shop.A.Rewind.g.cs", "Shop.B.Rewind.g.cs" }, run.Sources.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(run.CompilationProblems);
    }

    [Fact]
    public void Emit_EveryShape_CallsOnlyTheContractsSeamWithGlobalNames()
    {
        var run = GeneratorHarness.Run("""
            using ViShap.Rewind;

            namespace Shop;

            [Rewindable]
            public partial class Order
            {
                public partial decimal Total { get; set; }
                public partial System.Collections.Generic.IReadOnlyList<string> Tags { get; set; }
                [RewindIgnore] public partial int Views { get; set; }
            }
            """);

        var source = Assert.Single(run.Sources).Value;

        Assert.DoesNotContain("ViShap.Rewind.Engine", source);
        Assert.DoesNotContain("Journal", source);
        Assert.DoesNotContain("Rewind.Current", source);
        Assert.DoesNotContain("ScopeContext", source);
        Assert.Contains("global::ViShap.Rewind.Contracts.RewindProperty.Write(", source);
        Assert.Contains("global::System.Collections.Generic.IReadOnlyList<string>", source);
        Assert.Contains("global::System.Action<global::Shop.Order, decimal>", source);
        Assert.DoesNotContain("\r", source);
    }

    private static void Verify(string snapshot, string hintName, params string[] sources)
    {
        var run = GeneratorHarness.Run(sources);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.AnalyzerDiagnostics);
        Assert.Empty(run.CompilationProblems);
        Snapshots.Verify(snapshot, run.Sources[hintName]);
    }
}
