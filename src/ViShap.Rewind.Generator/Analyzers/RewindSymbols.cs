using Microsoft.CodeAnalysis;

namespace ViShap.Rewind.Generator.Analyzers;

/// <summary>The Rewind types of one compilation, found once at its start.</summary>
internal sealed class RewindSymbols
{
    private RewindSymbols(INamedTypeSymbol rewind, INamedTypeSymbol scope, INamedTypeSymbol cell)
    {
        Rewind = rewind;
        Scope = scope;
        Cell = cell;
    }

    /// <summary>Gets <c>ViShap.Rewind.Rewind</c>.</summary>
    public INamedTypeSymbol Rewind { get; }

    /// <summary>Gets <c>ViShap.Rewind.RewindScope</c>.</summary>
    public INamedTypeSymbol Scope { get; }

    /// <summary>Gets the definition of <c>ViShap.Rewind.RewindCell&lt;T&gt;</c>.</summary>
    public INamedTypeSymbol Cell { get; }

    /// <summary>Returns the symbols, or <see langword="null"/> when the compilation does not reference Core.</summary>
    public static RewindSymbols? From(Compilation compilation)
    {
        var rewind = compilation.GetTypeByMetadataName("ViShap.Rewind.Rewind");
        var scope = compilation.GetTypeByMetadataName("ViShap.Rewind.RewindScope");
        var cell = compilation.GetTypeByMetadataName("ViShap.Rewind.RewindCell`1");
        return rewind is null || scope is null || cell is null ? null : new RewindSymbols(rewind, scope, cell);
    }

    /// <summary>Returns whether <paramref name="method"/> is <c>Rewind.Begin()</c>.</summary>
    public bool IsBegin(IMethodSymbol? method) =>
        method is { Name: "Begin" } && SymbolEqualityComparer.Default.Equals(method.ContainingType, Rewind);

    /// <summary>Returns whether <paramref name="method"/> is an overload of <c>Rewind.Run</c>.</summary>
    public bool IsRun(IMethodSymbol? method) =>
        method is { Name: "Run" } && SymbolEqualityComparer.Default.Equals(method.ContainingType, Rewind);

    /// <summary>Returns whether <paramref name="method"/> registers an effect: <c>AfterCommit</c>, <c>AfterRollback</c> or <c>Effect</c>.</summary>
    public bool IsEffectRegistration(IMethodSymbol? method) =>
        method is { Name: "AfterCommit" or "AfterRollback" or "Effect" } && SymbolEqualityComparer.Default.Equals(method.ContainingType, Scope);
}
