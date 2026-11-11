using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ViShap.Rewind.Generator.Analyzers;

/// <summary>
/// RWD1001: the scope returned by <c>Rewind.Begin()</c> is discarded, or held by a local that is never disposed and
/// never leaves the method. A scope declared with <c>using</c> and never committed is a deliberate rollback and is not
/// reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ScopeLeakAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.ScopeLeak);

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

            start.RegisterOperationAction(operationContext => Analyze(operationContext, symbols), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, RewindSymbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!symbols.IsBegin(invocation.TargetMethod))
        {
            return;
        }

        var parent = invocation.Parent;
        while (parent is IConversionOperation or IParenthesizedOperation)
        {
            parent = parent.Parent;
        }

        if (parent is IExpressionStatementOperation)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.ScopeLeak, invocation.Syntax.GetLocation()));
            return;
        }

        if (parent is IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator }
            && declarator.Syntax is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax statement } }
            && !statement.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)
            && !DisposedOrEscapes(declarator.Symbol, statement, context.Operation.SemanticModel!, context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.ScopeLeak, invocation.Syntax.GetLocation()));
        }
    }

    private static bool DisposedOrEscapes(ILocalSymbol local, SyntaxNode declaration, SemanticModel model, System.Threading.CancellationToken cancellationToken)
    {
        var body = declaration.Parent;
        while (body is not null and not BlockSyntax and not CompilationUnitSyntax and not SwitchSectionSyntax)
        {
            body = body.Parent;
        }

        if (body is CompilationUnitSyntax)
        {
            body = declaration.SyntaxTree.GetRoot(cancellationToken);
        }

        foreach (var identifier in (body ?? declaration).DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText != local.Name
                || !SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, cancellationToken).Symbol, local))
            {
                continue;
            }

            if (identifier.Parent is MemberAccessExpressionSyntax access && access.Expression == identifier)
            {
                if (access.Name.Identifier.ValueText == "Dispose")
                {
                    return true;
                }

                continue;
            }

            if (identifier.Parent is ConditionalAccessExpressionSyntax conditional && conditional.Expression == identifier)
            {
                if (conditional.WhenNotNull is InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax { Name.Identifier.ValueText: "Dispose" } })
                {
                    return true;
                }

                continue;
            }

            return true;
        }

        return false;
    }
}
