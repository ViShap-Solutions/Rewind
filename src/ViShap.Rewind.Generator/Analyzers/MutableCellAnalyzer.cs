using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ViShap.Rewind.Generator.Analyzers;

/// <summary>RWD1004: a <c>RewindCell&lt;T&gt;</c> created for a mutable reference type, of which only the reference is tracked.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MutableCellAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.MutableCell);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (RewindSymbols.From(start.Compilation) is not { } symbols)
            {
                return;
            }

            start.RegisterOperationAction(
                operationContext =>
                {
                    var creation = (IObjectCreationOperation)operationContext.Operation;
                    if (creation.Type is INamedTypeSymbol { TypeArguments.Length: 1 } type
                        && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, symbols.Cell)
                        && TypeRules.IsMutableReference(type.TypeArguments[0]))
                    {
                        operationContext.ReportDiagnostic(Diagnostic.Create(
                            Descriptors.MutableCell,
                            creation.Syntax.GetLocation(),
                            type.TypeArguments[0].ToDisplayString()));
                    }
                },
                OperationKind.ObjectCreation);
        });
    }
}
