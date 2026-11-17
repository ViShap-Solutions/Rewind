using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ViShap.Rewind.Generator.Analyzers;

namespace ViShap.Rewind.Generator.CodeFixes;

/// <summary>
/// Fixes RWD1002 by moving the call into <c>scope.AfterCommit(() => …)</c>, so it runs only once the change is kept.
/// Offered only for a call whose result is discarded, when a scope variable is in reach.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(WrapInAfterCommitCodeFix))]
[Shared]
public sealed class WrapInAfterCommitCodeFix : CodeFixProvider
{
    /// <summary>The title of the fix.</summary>
    public const string Title = "Run it after the commit";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("RWD1002");

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null || RewindSymbols.From(model.Compilation) is not { } symbols)
        {
            return;
        }

        var invocation = root.FindNode(context.Span, getInnermostNodeForTie: true).AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation?.Parent is not ExpressionStatementSyntax statement
            || ScopeRegion.Of(invocation, model, symbols, context.CancellationToken) is not { ScopeName: { } scope })
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                Title,
                _ =>
                {
                    var replacement = SyntaxFactory.ParseStatement($"{scope}.AfterCommit(() => {invocation.WithoutTrivia()});")
                        .WithTriviaFrom(statement);
                    return Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(statement, replacement)));
                },
                equivalenceKey: Title),
            context.Diagnostics);
    }
}
