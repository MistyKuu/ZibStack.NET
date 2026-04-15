using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZibStack.NET.Aop.Analyzers;

namespace ZibStack.NET.Aop.CodeFixes;

/// <summary>
/// Code fix for AOP0014 — replaces a non-positive <c>TimeoutMs</c> with <c>30_000</c>
/// (the attribute's default of 30 seconds).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class FixTimeoutValueCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Diagnostics.TimeoutValueId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var attr = root.FindNode(diagnostic.Location.SourceSpan)
            .AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
        var arg = attr is null ? null : SetAttributeArgumentHelper.FindNamedArg(attr, "TimeoutMs");
        if (arg is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Set TimeoutMs to 30000",
                createChangedDocument: ct => SetAttributeArgumentHelper.SetNumericArgAsync(
                    context.Document, arg, SyntaxFactory.Literal(30_000), ct),
                equivalenceKey: "AOP_SetTimeoutMs"),
            diagnostic);
    }
}
