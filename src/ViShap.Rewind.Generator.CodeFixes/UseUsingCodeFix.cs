using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ViShap.Rewind.Generator.CodeFixes;

/// <summary>Fixes RWD1001 by declaring the scope with <c>using</c>, so it is disposed when the block ends.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseUsingCodeFix))]
[Shared]
public sealed class UseUsingCodeFix : CodeFixProvider
{
    /// <summary>The title of the fix.</summary>
    public const string Title = "Declare the scope with 'using'";

    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("RWD1001");

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var statement = root?.FindNode(context.Span).AncestorsAndSelf().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
        if (root is null || statement is null || statement.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)
            || statement.Parent is not (BlockSyntax or GlobalStatementSyntax or SwitchSectionSyntax))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                Title,
                _ =>
                {
                    var usingKeyword = SyntaxFactory.Token(statement.GetLeadingTrivia(), SyntaxKind.UsingKeyword, SyntaxFactory.TriviaList(SyntaxFactory.Space));
                    var replacement = statement.WithoutLeadingTrivia().WithUsingKeyword(usingKeyword);
                    return Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(statement, replacement)));
                },
                equivalenceKey: Title),
            context.Diagnostics);
    }
}
