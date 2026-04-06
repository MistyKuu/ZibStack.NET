using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Log.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseLogExCodeFixProvider)), Shared]
public sealed class UseLogExCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(UseLogExAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var node = root.FindNode(diagnosticSpan);
        if (node is not SimpleNameSyntax identifierName) return;

        var currentName = identifierName.Identifier.Text;
        var newName = currentName + "Ex";

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Use '{newName}' instead",
                createChangedDocument: ct => ReplaceMethodNameAsync(context.Document, identifierName, newName, ct),
                equivalenceKey: UseLogExAnalyzer.DiagnosticId),
            diagnostic);
    }

    private static async Task<Document> ReplaceMethodNameAsync(
        Document document, SimpleNameSyntax identifierName, string newName, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        var newIdentifier = SyntaxFactory.IdentifierName(newName)
            .WithTriviaFrom(identifierName);

        var newRoot = root.ReplaceNode(identifierName, newIdentifier);
        return document.WithSyntaxRoot(newRoot);
    }
}
