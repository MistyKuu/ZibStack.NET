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
/// Code fix for AOP1001 — inserts the missing <c>[Aspect]</c> attribute on the type
/// declaration. Uses the short name (e.g. <c>[Log]</c>) on the assumption that the
/// containing namespace is already imported in real consumer code; if it isn't, the
/// compiler's own missing-using diagnostic + light-bulb adds the using.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class AddMissingAspectCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Diagnostics.MissingRequiredAspectId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        if (!diagnostic.Properties.TryGetValue("RequiredAspectShortName", out var shortName) || shortName is null)
            return;

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var typeDecl = node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (typeDecl is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Add [{shortName}] attribute",
                createChangedDocument: ct => AddAttributeAsync(context.Document, typeDecl, shortName, ct),
                equivalenceKey: $"AOP_AddRequired_{shortName}"),
            diagnostic);
    }

    private static async Task<Document> AddAttributeAsync(Document document, TypeDeclarationSyntax typeDecl, string shortName, CancellationToken ct)
    {
        var attribute = SyntaxFactory.Attribute(SyntaxFactory.ParseName(shortName));
        var newAttrList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute))
            // Mimic how a developer would type it — own line directly above the type, with
            // matching leading trivia (so indentation lines up).
            .WithLeadingTrivia(typeDecl.GetLeadingTrivia())
            // Hard-code "\n" rather than Environment.NewLine — the analyzer SDK bans
            // Environment access (RS1035) and Roslyn's formatter normalizes either way.
            .WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"));

        // The new attribute list inherits the leading trivia from the type, so strip it
        // off the type itself or we'd end up with the comment/attributes block printed twice.
        var newTypeDecl = typeDecl
            .WithLeadingTrivia(SyntaxFactory.TriviaList())
            .WithAttributeLists(typeDecl.AttributeLists.Insert(0, newAttrList));

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var newRoot = root!.ReplaceNode(typeDecl, newTypeDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}
