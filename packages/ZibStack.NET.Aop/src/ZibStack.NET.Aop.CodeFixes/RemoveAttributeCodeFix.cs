using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZibStack.NET.Aop.Analyzers;

namespace ZibStack.NET.Aop.CodeFixes;

/// <summary>
/// Generic "the aspect doesn't apply here, remove it" code fix. Handles the diagnostics
/// where the only sensible repair is to drop the attribute (anything else would change
/// the user's intent or require non-trivial refactoring):
///
/// <list type="bullet">
///   <item>AOP0001 — aspect on a static method</item>
///   <item>AOP0010 — [Cache] on a void / non-generic Task method</item>
///   <item>AOP0016 — [Validate] on a parameterless method</item>
/// </list>
///
/// If the attribute shares an attribute list (e.g. <c>[A, B, C]</c>) only the offending
/// attribute is removed; if it owns the whole list, the entire <c>[...]</c> goes too,
/// taking its trailing newline with it.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class RemoveAttributeCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(
            Diagnostics.StaticMethodId,
            Diagnostics.CacheNonReturningId,
            Diagnostics.ValidateNoParametersId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        // For AOP0001/AOP0016 the diagnostic lands on the method symbol; for AOP0010 it
        // lands on the attribute itself. Walk the syntax to find the AttributeSyntax to
        // remove. AOP0001 is reported with the method name as location, so we grab the
        // first matching aspect attribute on that method.
        var attr = node.AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
        if (attr is null && node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } method)
        {
            var aspectName = diagnostic.GetMessage().Contains("[Cache]") ? "Cache"
                          : diagnostic.GetMessage().Contains("[Validate]") ? "Validate"
                          : null;

            attr = method.AttributeLists
                .SelectMany(al => al.Attributes)
                .FirstOrDefault(a =>
                    aspectName is null
                    || a.Name.ToString().EndsWith(aspectName, System.StringComparison.Ordinal)
                    || a.Name.ToString().EndsWith(aspectName + "Attribute", System.StringComparison.Ordinal));
        }
        if (attr is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Remove [{attr.Name}]",
                createChangedDocument: ct => RemoveAttributeAsync(context.Document, attr, ct),
                equivalenceKey: "AOP_RemoveAttribute"),
            diagnostic);
    }

    private static async Task<Document> RemoveAttributeAsync(Document document, AttributeSyntax attr, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        var attrList = (AttributeListSyntax)attr.Parent!;
        SyntaxNode newRoot;
        if (attrList.Attributes.Count == 1)
        {
            // Sole attribute in its `[...]` list — drop the whole list and its trivia
            // so we don't leave a blank line where the attribute used to be.
            newRoot = root.RemoveNode(attrList, SyntaxRemoveOptions.KeepNoTrivia)!;
        }
        else
        {
            // Just remove the one attribute, leaving sibling attributes (and the brackets) intact.
            newRoot = root.RemoveNode(attr, SyntaxRemoveOptions.KeepNoTrivia)!;
        }

        return document.WithSyntaxRoot(newRoot);
    }
}
