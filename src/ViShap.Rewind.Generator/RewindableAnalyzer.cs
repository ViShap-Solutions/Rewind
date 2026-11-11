using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ViShap.Rewind.Generator;

/// <summary>
/// Reports what the generator cannot track in a class marked <c>[Rewindable]</c> (RWD0001–RWD0005), at the exact
/// source location, so a warning can be suppressed like any other.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RewindableAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
        Descriptors.NotPartial,
        Descriptors.UntrackedProperty,
        Descriptors.UnsupportedAccessor,
        Descriptors.MutableReference,
        Descriptors.UnsupportedShape);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(
            static symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (!RewindPlanner.IsRewindable(type))
                {
                    return;
                }

                foreach (var diagnostic in RewindPlanner.Diagnose(type, symbolContext.CancellationToken))
                {
                    symbolContext.ReportDiagnostic(diagnostic);
                }
            },
            SymbolKind.NamedType);
    }
}
