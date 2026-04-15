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
/// Code fix for AOP1002 — appends the missing interface to the type's base list.
/// Doesn't generate the implementation members; the C# compiler's own CS0535 light-bulb
/// ("Implement interface") then takes over for stubbing the methods/properties.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class AddMissingInterfaceCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Diagnostics.MissingRequiredImplementationId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        if (!diagnostic.Properties.TryGetValue("RequiredInterfaceShortName", out var shortName) || shortName is null)
            return;

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var typeDecl = node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (typeDecl is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Implement {shortName}",
                createChangedDocument: ct => AddInterfaceAsync(context.Document, typeDecl, shortName, ct),
                equivalenceKey: $"AOP_AddInterface_{shortName}"),
            diagnostic);
    }

    private static async Task<Document> AddInterfaceAsync(Document document, TypeDeclarationSyntax typeDecl, string shortName, CancellationToken ct)
    {
        var newTypeRef = SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(shortName));

        TypeDeclarationSyntax newTypeDecl;
        if (typeDecl.BaseList is null)
        {
            // No existing base list — create one. Need a leading space before ":" and the
            // identifier preceding it (the type name) to keep its trailing whitespace.
            var baseList = SyntaxFactory.BaseList(
                SyntaxFactory.Token(SyntaxKind.ColonToken).WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia(SyntaxFactory.Space),
                SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(newTypeRef));

            // Trailing trivia of the identifier (or type-parameter-list) becomes leading
            // of the new ":". Move it onto the base list to keep the original whitespace.
            newTypeDecl = typeDecl.WithBaseList(baseList);
        }
        else
        {
            // Append to existing base list with a comma separator.
            var newBaseList = typeDecl.BaseList.WithTypes(typeDecl.BaseList.Types.Add(newTypeRef));
            newTypeDecl = typeDecl.WithBaseList(newBaseList);
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var newRoot = root!.ReplaceNode(typeDecl, newTypeDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}
