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
/// Code fix for AOP0015 — appends a defaulted
/// <c>CancellationToken cancellationToken = default</c> parameter to a method that has
/// <c>[Timeout]</c> but no way to observe the cancellation. With the parameter present,
/// the generator threads a linked CTS through it and <c>TimeoutHandler</c> signals it
/// via <c>CancelAfter(timeoutMs)</c> — so the body's awaits actually abort cooperatively.
///
/// <para>
/// The developer still has to forward the parameter to their internal awaits
/// (<c>Task.Delay(ms, cancellationToken)</c>, <c>HttpClient.GetAsync(url, cancellationToken)</c>,
/// etc.) — the code fix can't safely guess where to inject it.
/// </para>
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class AddCancellationTokenCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Diagnostics.TimeoutNoCancellationTokenId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var method = root.FindNode(diagnostic.Location.SourceSpan)
            .AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is null) return;

        // Defensive: if the method already has a CT parameter, don't double-add (analyzer
        // shouldn't fire in that case, but tolerate the race).
        if (method.ParameterList.Parameters.Any(p => p.Type?.ToString().EndsWith("CancellationToken") == true))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add CancellationToken parameter",
                createChangedDocument: ct => AddParameterAsync(context.Document, method, ct),
                equivalenceKey: "AOP_AddCancellationToken"),
            diagnostic);
    }

    private static async Task<Document> AddParameterAsync(Document document, MethodDeclarationSyntax method, CancellationToken ct)
    {
        var newParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier("cancellationToken"))
            .WithType(SyntaxFactory.ParseTypeName("global::System.Threading.CancellationToken "))
            .WithDefault(SyntaxFactory.EqualsValueClause(
                SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression,
                    SyntaxFactory.Token(SyntaxKind.DefaultKeyword))));

        var newParameterList = method.ParameterList.AddParameters(newParam);
        var newMethod = method.WithParameterList(newParameterList);

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        return document.WithSyntaxRoot(root!.ReplaceNode(method, newMethod));
    }
}
