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

        // Same fix shape for either a class/interface declaration or a method
        // declaration — both inherit MemberDeclarationSyntax which exposes AttributeLists
        // and LeadingTrivia in identical ways.
        var memberDecl = node.AncestorsAndSelf()
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(m => m is BaseTypeDeclarationSyntax or MethodDeclarationSyntax);
        if (memberDecl is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Add [{shortName}] attribute",
                createChangedDocument: ct => AddAttributeAsync(context.Document, memberDecl, shortName, ct),
                equivalenceKey: $"AOP_AddRequired_{shortName}"),
            diagnostic);
    }

    private static async Task<Document> AddAttributeAsync(Document document, MemberDeclarationSyntax memberDecl, string shortName, CancellationToken ct)
    {
        var attribute = SyntaxFactory.Attribute(SyntaxFactory.ParseName(shortName));

        // Detect line-ending and indent from the member's existing leading trivia so we
        // match the source's style instead of fighting it on Windows (where Roslyn's
        // Formatter would normalize to CRLF and break LF-only test expectations).
        var leading = memberDecl.GetLeadingTrivia();
        var newline = ExtractNewline(leading);
        var indent = ExtractIndent(leading);

        // Move the member's leading trivia (newline + indent that positioned the member)
        // onto the new attribute list, so the attribute appears where the member used to
        // start. Then re-emit newline + indent as the attribute list's trailing trivia so
        // the original member content lands on the next line at the same column.
        var newAttrList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute))
            .WithLeadingTrivia(leading)
            .WithTrailingTrivia(newline, indent);

        // Strip the member's own leading trivia — otherwise it would print twice (once on
        // the attr list, once on the modifier the trivia originally sat on).
        var newMemberDecl = memberDecl
            .WithLeadingTrivia(SyntaxFactory.TriviaList())
            .WithAttributeLists(memberDecl.AttributeLists.Insert(0, newAttrList));

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var newRoot = root!.ReplaceNode(memberDecl, newMemberDecl);
        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// Returns the first end-of-line trivia found in <paramref name="leading"/>, or a
    /// LF fallback when none is present (top-of-file member).
    /// </summary>
    private static SyntaxTrivia ExtractNewline(SyntaxTriviaList leading)
    {
        foreach (var trivia in leading)
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                return trivia;
        }
        return SyntaxFactory.LineFeed;
    }

    /// <summary>
    /// Returns the trailing whitespace of <paramref name="leading"/> — i.e. the indent
    /// applied to the member's first token — or empty trivia if the member is not indented.
    /// </summary>
    private static SyntaxTrivia ExtractIndent(SyntaxTriviaList leading)
    {
        for (int i = leading.Count - 1; i >= 0; i--)
        {
            var trivia = leading[i];
            if (trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                return trivia;
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                break;
        }
        return SyntaxFactory.Whitespace(string.Empty);
    }
}
