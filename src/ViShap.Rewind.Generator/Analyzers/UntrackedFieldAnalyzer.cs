using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ViShap.Rewind.Generator.Analyzers;

/// <summary>
/// RWD1005: inside a scope, in code of a <c>[Rewindable]</c> type, an instance field of <c>this</c> is assigned,
/// compound-assigned, incremented or decremented. A field is not tracked, so a rollback does not restore it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UntrackedFieldAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.UntrackedField);

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
                    var target = operationContext.Operation switch
                    {
                        IAssignmentOperation assignment => assignment.Target,
                        IIncrementOrDecrementOperation step => step.Target,
                        _ => null,
                    };

                    if (target is IFieldReferenceOperation { Field.IsStatic: false, Field.IsImplicitlyDeclared: false } field
                        && field.Instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }
                        && TypeRules.IsRewindable(field.Field.ContainingType)
                        && ScopeRegion.Of(target.Syntax, operationContext.Operation.SemanticModel!, symbols, operationContext.CancellationToken) is not null)
                    {
                        operationContext.ReportDiagnostic(Diagnostic.Create(Descriptors.UntrackedField, target.Syntax.GetLocation(), field.Field.Name));
                    }
                },
                OperationKind.SimpleAssignment,
                OperationKind.CompoundAssignment,
                OperationKind.CoalesceAssignment,
                OperationKind.Increment,
                OperationKind.Decrement);
        });
    }
}
