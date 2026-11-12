using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ViShap.Rewind.Generator.Analyzers;

/// <summary>
/// RWD1006: <c>Task.WhenAll</c>, <c>Task.WhenAny</c>, <c>Task.Run</c>, <c>TaskFactory.StartNew</c> or a <c>Parallel</c>
/// method called inside a block passed to <c>Rewind.RunAsync</c>: the branches would share the scope's journal.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ParallelBranchAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.ParallelBranch);

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

            var task = start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
            var factory = start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.TaskFactory");
            var parallel = start.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Parallel");

            start.RegisterOperationAction(
                operationContext =>
                {
                    var invocation = (IInvocationOperation)operationContext.Operation;
                    var method = invocation.TargetMethod;
                    var type = method.ContainingType?.OriginalDefinition;
                    var branches = method.Name switch
                    {
                        "WhenAll" or "WhenAny" or "Run" => SymbolEqualityComparer.Default.Equals(type, task),
                        "StartNew" => SymbolEqualityComparer.Default.Equals(type, factory) || SymbolEqualityComparer.Default.Equals(type?.BaseType, factory),
                        "For" or "ForEach" or "ForAsync" or "ForEachAsync" or "Invoke" => SymbolEqualityComparer.Default.Equals(type, parallel),
                        _ => false,
                    };

                    if (branches && InAsynchronousScope(invocation.Syntax, invocation.SemanticModel!, symbols, operationContext.CancellationToken))
                    {
                        operationContext.ReportDiagnostic(Diagnostic.Create(
                            Descriptors.ParallelBranch,
                            invocation.Syntax.GetLocation(),
                            method.ContainingType!.Name + "." + method.Name));
                    }
                },
                OperationKind.Invocation);
        });
    }

    /// <summary>
    /// Returns whether <paramref name="node"/> lies lexically inside a block passed to <c>Rewind.RunAsync</c>, not inside a
    /// block passed to <c>AfterCommit</c>, <c>AfterRollback</c> or <c>Effect</c>; the search stops at a member or local function.
    /// </summary>
    private static bool InAsynchronousScope(SyntaxNode node, SemanticModel model, RewindSymbols symbols, CancellationToken cancellationToken)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case AnonymousFunctionExpressionSyntax function:
                    var invoked = ScopeRegion.Invoked(function, model, cancellationToken);
                    if (symbols.IsEffectRegistration(invoked))
                    {
                        return false;
                    }

                    if (symbols.IsRunAsync(invoked))
                    {
                        return true;
                    }

                    break;

                case LocalFunctionStatementSyntax:
                case MemberDeclarationSyntax:
                case AccessorDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }
}
