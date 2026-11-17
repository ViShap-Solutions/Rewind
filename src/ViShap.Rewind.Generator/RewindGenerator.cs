using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ViShap.Rewind.Generator;

/// <summary>
/// Writes the tracked implementation of the partial properties of a class marked <c>[Rewindable]</c>. What it cannot
/// track is reported by <see cref="RewindableAnalyzer"/>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class RewindGenerator : IIncrementalGenerator
{
    /// <summary>The tracking name of the pipeline step that plans each rewindable type.</summary>
    public const string PlanStep = "RewindablePlan";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var plans = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                RewindPlanner.RewindableAttribute,
                static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                static (attributed, cancellationToken) => RewindPlanner.Plan((INamedTypeSymbol)attributed.TargetSymbol, cancellationToken))
            .WithTrackingName(PlanStep);

        context.RegisterSourceOutput(plans, static (output, plan) =>
        {
            if (plan.Properties.Length > 0)
            {
                output.AddSource(plan.HintName, SourceText.From(RewindEmitter.Emit(plan), System.Text.Encoding.UTF8));
            }
        });
    }
}
