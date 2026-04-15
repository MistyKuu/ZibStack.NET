using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Aop.CodeFixes;

/// <summary>
/// Shared helper for code fixes that replace a single named argument's value on an
/// attribute application (e.g. <c>[Retry(MaxAttempts = 0)]</c> → <c>[Retry(MaxAttempts = 3)]</c>).
/// All numeric-arg fixes follow the same Roslyn dance, so factoring it out keeps each
/// individual fix to a few lines.
/// </summary>
internal static class SetAttributeArgumentHelper
{
    public static async Task<Document> SetNumericArgAsync(
        Document document,
        AttributeArgumentSyntax arg,
        SyntaxToken literalToken,
        CancellationToken ct)
    {
        var newExpr = SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, literalToken);
        var newArg = arg.WithExpression(newExpr);
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        return document.WithSyntaxRoot(root!.ReplaceNode(arg, newArg));
    }

    public static AttributeArgumentSyntax? FindNamedArg(AttributeSyntax attr, string name) =>
        attr.ArgumentList?.Arguments.FirstOrDefault(a =>
            a.NameEquals?.Name.Identifier.ValueText == name);
}
