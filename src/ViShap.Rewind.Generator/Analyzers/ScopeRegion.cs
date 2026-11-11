using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ViShap.Rewind.Generator.Analyzers;

/// <summary>A scope that a piece of code runs in, as seen in the source.</summary>
/// <param name="ScopeName">The name through which the code can reach the scope, or <see langword="null"/> when it has none.</param>
internal sealed record ScopeRegion(string? ScopeName)
{
    /// <summary>
    /// Returns the scope <paramref name="node"/> runs in directly, or <see langword="null"/>. Code runs in a scope when it is
    /// in the block passed to <c>Rewind.Run</c>, in the body of <c>using (Rewind.Begin())</c>, or after a local initialised
    /// with <c>Rewind.Begin()</c> in the same block. Code in a block passed to <c>AfterCommit</c>, <c>AfterRollback</c> or
    /// <c>Effect</c> runs outside the scope's journal and is not in it; the search stops at a member or local function.
    /// </summary>
    public static ScopeRegion? Of(SyntaxNode node, SemanticModel model, RewindSymbols symbols, CancellationToken cancellationToken)
    {
        SyntaxNode? previous = null;
        for (var current = node; current is not null; previous = current, current = current.Parent)
        {
            switch (current)
            {
                case AnonymousFunctionExpressionSyntax function when Invoked(function, model, cancellationToken) is { } invoked:
                    if (symbols.IsEffectRegistration(invoked))
                    {
                        return null;
                    }

                    if (symbols.IsRun(invoked))
                    {
                        return new ScopeRegion(ScopeParameter(function, model, symbols, cancellationToken));
                    }

                    break;

                case UsingStatementSyntax usingStatement when previous == usingStatement.Statement:
                    if (usingStatement.Expression is { } expression && IsBegin(expression, model, symbols, cancellationToken))
                    {
                        return new ScopeRegion(ScopeName: null);
                    }

                    if (usingStatement.Declaration?.Variables.FirstOrDefault(variable => IsBegin(variable.Initializer?.Value, model, symbols, cancellationToken)) is { } declared)
                    {
                        return new ScopeRegion(declared.Identifier.ValueText);
                    }

                    break;

                case BlockSyntax block when previous is StatementSyntax statement:
                    if (BeginLocalBefore(block.Statements.TakeWhile(candidate => candidate != statement), model, symbols, cancellationToken) is { } local)
                    {
                        return local;
                    }

                    break;

                case CompilationUnitSyntax unit when previous is GlobalStatementSyntax global:
                    var earlier = unit.Members.TakeWhile(member => member != global).OfType<GlobalStatementSyntax>().Select(member => member.Statement);
                    if (BeginLocalBefore(earlier, model, symbols, cancellationToken) is { } topLevel)
                    {
                        return topLevel;
                    }

                    break;

                case LocalFunctionStatementSyntax:
                case MemberDeclarationSyntax:
                case AccessorDeclarationSyntax:
                    return null;
            }
        }

        return null;
    }

    private static ScopeRegion? BeginLocalBefore(
        System.Collections.Generic.IEnumerable<StatementSyntax> statements,
        SemanticModel model,
        RewindSymbols symbols,
        CancellationToken cancellationToken)
    {
        ScopeRegion? found = null;
        foreach (var statement in statements)
        {
            if (statement is LocalDeclarationStatementSyntax declaration
                && declaration.Declaration.Variables.FirstOrDefault(variable => IsBegin(variable.Initializer?.Value, model, symbols, cancellationToken)) is { } variable)
            {
                found = new ScopeRegion(variable.Identifier.ValueText);
            }
        }

        return found;
    }

    private static IMethodSymbol? Invoked(AnonymousFunctionExpressionSyntax function, SemanticModel model, CancellationToken cancellationToken) =>
        function.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } }
            ? model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol
            : null;

    private static string? ScopeParameter(AnonymousFunctionExpressionSyntax function, SemanticModel model, RewindSymbols symbols, CancellationToken cancellationToken)
    {
        var parameter = function switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter,
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.FirstOrDefault(),
            _ => null,
        };

        return parameter is not null
            && model.GetDeclaredSymbol(parameter, cancellationToken) is { } symbol
            && SymbolEqualityComparer.Default.Equals(symbol.Type, symbols.Scope)
            && !parameter.Identifier.IsKind(SyntaxKind.UnderscoreToken)
            && parameter.Identifier.ValueText != "_"
                ? parameter.Identifier.ValueText
                : null;
    }

    private static bool IsBegin(ExpressionSyntax? expression, SemanticModel model, RewindSymbols symbols, CancellationToken cancellationToken) =>
        expression is InvocationExpressionSyntax invocation
        && symbols.IsBegin(model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol);
}
