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
/// Code fix for AOP1004 — generates a stub public constructor matching the required
/// parameter list reported by the analyzer.
///
/// <para>
/// The signature comes from the diagnostic message itself (parsed from the format
/// "(type1, type2, ...)"), so the fix doesn't need to reach back into the symbol
/// table — it works as long as the message format stays in sync with
/// <see cref="Diagnostics.MissingRequiredConstructor"/>. Body is a single
/// <c>throw new System.NotImplementedException()</c>; the developer fills it in.
/// </para>
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class AddRequiredConstructorCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Diagnostics.MissingRequiredConstructorId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var typeDecl = node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (typeDecl is null) return;

        // Pull the required signature out of the diagnostic message: "...requires a public
        // constructor '(System.IServiceProvider, int)'..." → ["System.IServiceProvider", "int"].
        var signature = ExtractSignatureFromMessage(diagnostic.GetMessage());
        if (signature is null) return;

        var titleSuffix = signature.Length == 0 ? "()" : "(" + string.Join(", ", signature) + ")";

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Add constructor {titleSuffix}",
                createChangedDocument: ct => AddCtorAsync(context.Document, typeDecl, signature, ct),
                equivalenceKey: $"AOP_AddCtor_{titleSuffix}"),
            diagnostic);
    }

    private static string[]? ExtractSignatureFromMessage(string message)
    {
        // The format set in Diagnostics.MissingRequiredConstructor is:
        //   "'{0}' derives from '{1}' which requires a public constructor '{2}'{3}"
        // where {2} looks like "(type1, type2)" or "()".
        var open = message.IndexOf("constructor '(", System.StringComparison.Ordinal);
        if (open < 0) return null;
        open += "constructor '(".Length;
        var close = message.IndexOf(")'", open, System.StringComparison.Ordinal);
        if (close < 0) return null;

        var inner = message.Substring(open, close - open);
        if (string.IsNullOrWhiteSpace(inner)) return System.Array.Empty<string>();
        return inner.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
    }

    private static async Task<Document> AddCtorAsync(Document document, TypeDeclarationSyntax typeDecl, string[] signature, CancellationToken ct)
    {
        // Build the constructor as a fully-formatted source string and parse it. Going
        // through SyntaxFactory + NormalizeWhitespace produces zero-indent member text
        // that doesn't sit naturally inside an existing class; rendering as a string
        // lets us control indentation exactly and preserve the source's line endings.
        var eol = DetectSourceEol(typeDecl);
        var typeName = typeDecl.Identifier.ValueText;

        var paramList = signature.Length == 0
            ? "()"
            : "(" + string.Join(", ", signature.Select((t, i) => $"{t} p{i}")) + ")";

        var ctorText =
            $"public {typeName}{paramList}{eol}" +
            $"    {{{eol}" +
            $"        throw new global::System.NotImplementedException();{eol}" +
            $"    }}{eol}";

        var ctorMember = SyntaxFactory.ParseMemberDeclaration(ctorText);
        if (ctorMember is not ConstructorDeclarationSyntax ctor) return document;

        // Indent the new ctor to match other members. If the class already has members,
        // copy their leading whitespace; otherwise default to 4 spaces.
        var indent = SyntaxFactory.Whitespace("    ");
        if (typeDecl.Members.Count > 0)
        {
            var firstMember = typeDecl.Members[0];
            foreach (var trivia in firstMember.GetLeadingTrivia())
            {
                if (trivia.IsKind(SyntaxKind.WhitespaceTrivia))
                {
                    indent = trivia;
                    break;
                }
            }
        }

        ctor = ctor.WithLeadingTrivia(indent);

        var newTypeDecl = typeDecl.WithMembers(typeDecl.Members.Insert(0, ctor));

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var newRoot = root!.ReplaceNode(typeDecl, newTypeDecl);
        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>Returns the first end-of-line trivia text in the document, or "\n" as fallback.</summary>
    private static string DetectSourceEol(SyntaxNode anchor)
    {
        foreach (var trivia in anchor.SyntaxTree.GetRoot().DescendantTrivia())
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                return trivia.ToFullString();
        }
        return "\n";
    }
}
