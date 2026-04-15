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
/// Code fix for AOP0013 — replaces a sub-1.0 <c>BackoffMultiplier</c> with <c>1.0</c>
/// (constant delay between retries). Doubles aren't a numeric literal token so we
/// can't use the shared int helper here.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class FixRetryBackoffCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Diagnostics.RetryBackoffId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var attr = root.FindNode(diagnostic.Location.SourceSpan)
            .AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
        var arg = attr is null ? null : SetAttributeArgumentHelper.FindNamedArg(attr, "BackoffMultiplier");
        if (arg is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Set BackoffMultiplier to 1.0",
                createChangedDocument: ct => SetBackoffAsync(context.Document, arg, ct),
                equivalenceKey: "AOP_SetBackoff1"),
            diagnostic);
    }

    private static async Task<Document> SetBackoffAsync(Document document, AttributeArgumentSyntax arg, CancellationToken ct)
    {
        // Use the explicit text "1.0" so the rewritten attribute keeps the double form
        // (SyntaxFactory.Literal(1.0) renders as "1", which is technically the same value
        // but reads less clearly next to BackoffMultiplier and may surprise readers).
        var newExpr = SyntaxFactory.LiteralExpression(
            SyntaxKind.NumericLiteralExpression,
            SyntaxFactory.Literal("1.0", 1.0));
        var newArg = arg.WithExpression(newExpr);
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        return document.WithSyntaxRoot(root!.ReplaceNode(arg, newArg));
    }
}
