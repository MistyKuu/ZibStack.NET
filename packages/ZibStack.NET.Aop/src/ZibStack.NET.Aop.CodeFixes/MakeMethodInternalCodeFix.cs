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
using ZibStack.NET.Aop.Analyzers;

namespace ZibStack.NET.Aop.CodeFixes;

/// <summary>
/// Code fix for AOP0002 — replaces a private/protected/private-protected modifier on a
/// method that carries an aspect with <c>internal</c>, the lowest accessibility the
/// generated interceptor can still call back into.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class MakeMethodInternalCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Diagnostics.PrivateOrProtectedId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var method = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Make method internal",
                createChangedDocument: ct => MakeInternalAsync(context.Document, method, ct),
                equivalenceKey: "AOP_MakeInternal"),
            diagnostic);
    }

    private static async Task<Document> MakeInternalAsync(Document document, MethodDeclarationSyntax method, CancellationToken ct)
    {
        var modifiersToRemove = new[]
        {
            SyntaxKind.PrivateKeyword,
            SyntaxKind.ProtectedKeyword,
        };

        var newModifiers = SyntaxFactory.TokenList(
            method.Modifiers.Where(m => !modifiersToRemove.Contains(m.Kind())));

        // Insert `internal` at the front, preserving any leading trivia from the original
        // first modifier (so blank lines / comments above the method survive the rewrite).
        var internalToken = SyntaxFactory.Token(SyntaxKind.InternalKeyword);
        if (method.Modifiers.Count > 0)
        {
            internalToken = internalToken.WithLeadingTrivia(method.Modifiers[0].LeadingTrivia)
                                         .WithTrailingTrivia(SyntaxFactory.Space);
        }
        else
        {
            // No prior modifiers — pull leading trivia off the return type instead.
            internalToken = internalToken.WithLeadingTrivia(method.ReturnType.GetLeadingTrivia())
                                         .WithTrailingTrivia(SyntaxFactory.Space);
        }

        newModifiers = newModifiers.Insert(0, internalToken);

        MethodDeclarationSyntax newMethod;
        if (method.Modifiers.Count == 0)
        {
            // Strip the leading trivia from the return type since `internal` now owns it.
            newMethod = method
                .WithReturnType(method.ReturnType.WithLeadingTrivia())
                .WithModifiers(newModifiers);
        }
        else
        {
            newMethod = method.WithModifiers(newModifiers);
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var newRoot = root!.ReplaceNode(method, newMethod);
        return document.WithSyntaxRoot(newRoot);
    }
}
