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
/// Code fix for AOP0011 — replaces the invalid <c>MaxAttempts = 0</c> (or negative) with
/// the default value <c>3</c>. Picks 3 (matching <c>RetryAttribute.MaxAttempts</c>'s
/// default) rather than 1 because anyone who typed <c>[Retry(...)]</c> at all is asking
/// for retries; "1 attempt" is just "no retry" and likely a typo.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class FixRetryMaxAttemptsCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Diagnostics.RetryMaxAttemptsId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var attr = node.AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
        if (attr?.ArgumentList is null) return;

        var maxAttemptsArg = attr.ArgumentList.Arguments.FirstOrDefault(a =>
            a.NameEquals?.Name.Identifier.ValueText == "MaxAttempts");
        if (maxAttemptsArg is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Set MaxAttempts to 3",
                createChangedDocument: ct => SetMaxAttemptsAsync(context.Document, maxAttemptsArg, ct),
                equivalenceKey: "AOP_SetMaxAttempts3"),
            diagnostic);
    }

    private static async Task<Document> SetMaxAttemptsAsync(Document document, AttributeArgumentSyntax arg, CancellationToken ct)
    {
        var newExpr = SyntaxFactory.LiteralExpression(
            SyntaxKind.NumericLiteralExpression,
            SyntaxFactory.Literal(3));
        var newArg = arg.WithExpression(newExpr);

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var newRoot = root!.ReplaceNode(arg, newArg);
        return document.WithSyntaxRoot(newRoot);
    }
}
