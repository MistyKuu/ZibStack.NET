using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZibStack.NET.Aop.Generator;

/// <summary>
/// Roslyn analyzer that validates <c>KeyTemplate</c> placeholders in <c>[Cache]</c> and
/// <c>[HybridCache]</c> attributes. Validates both root parameter names and nested property
/// paths (e.g. <c>{req.Customer.Id}</c> checks that <c>req</c> is a parameter, <c>Customer</c>
/// is a property on its type, and <c>Id</c> is a property on <c>Customer</c>'s type).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class KeyTemplateAnalyzer : DiagnosticAnalyzer
{
    public const string InvalidPlaceholderId = "ZBAOP001";
    public const string InvalidPropertyPathId = "ZBAOP002";

    private static readonly DiagnosticDescriptor InvalidPlaceholderRule = new(
        InvalidPlaceholderId,
        title: "KeyTemplate placeholder does not match any parameter",
        messageFormat: "KeyTemplate placeholder '{{{0}}}' — root name '{1}' does not match any parameter of method '{2}'. Available parameters: {3}.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidPropertyPathRule = new(
        InvalidPropertyPathId,
        title: "KeyTemplate property path is invalid",
        messageFormat: "KeyTemplate placeholder '{{{0}}}': '{1}' does not have a property '{2}'",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly string[] AttributeNames =
    {
        "ZibStack.NET.Aop.CacheAttribute",
        "ZibStack.NET.Aop.HybridCacheAttribute",
    };

    // Matches {expr} but not {{ or }} (escaped braces)
    private static readonly Regex PlaceholderRegex = new(@"(?<!\{)\{([^{}]+)\}(?!\})", RegexOptions.Compiled);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(InvalidPlaceholderRule, InvalidPropertyPathRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        foreach (var attr in method.GetAttributes())
        {
            if (!IsTargetAttribute(attr)) continue;
            AnalyzeAttribute(context, method, attr);
        }

        // Check class-level attributes too
        if (method.ContainingType is not null)
        {
            foreach (var attr in method.ContainingType.GetAttributes())
            {
                if (!IsTargetAttribute(attr)) continue;
                AnalyzeAttribute(context, method, attr);
            }
        }
    }

    private static bool IsTargetAttribute(AttributeData attr)
    {
        var fullName = attr.AttributeClass?.ToDisplayString();
        return fullName is not null && AttributeNames.Any(n => n == fullName);
    }

    private static void AnalyzeAttribute(SymbolAnalysisContext context, IMethodSymbol method, AttributeData attr)
    {
        string? keyTemplate = null;
        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg.Key == "KeyTemplate" && namedArg.Value.Value is string s)
            {
                keyTemplate = s;
                break;
            }
        }

        if (keyTemplate is null) return;

        var paramMap = method.Parameters.ToImmutableDictionary(p => p.Name, p => p.Type);
        var available = string.Join(", ", paramMap.Keys);

        foreach (Match match in PlaceholderRegex.Matches(keyTemplate))
        {
            var expr = match.Groups[1].Value; // e.g. "req.Customer.Id" or "id"
            var parts = expr.Split('.');
            var root = parts[0].Trim();

            var location = attr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? method.Locations.FirstOrDefault()
                ?? Location.None;

            // Check root parameter
            if (!paramMap.TryGetValue(root, out var currentType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidPlaceholderRule, location,
                    expr, root, method.Name, available));
                continue;
            }

            // Walk property path: req.Customer.Id → check Customer on req's type, then Id on Customer's type
            var pathSoFar = root;
            for (int i = 1; i < parts.Length; i++)
            {
                var propName = parts[i].Trim();
                var resolved = FindProperty(currentType, propName);

                if (resolved is null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidPropertyPathRule, location,
                        expr, pathSoFar, propName));
                    break;
                }

                pathSoFar = $"{pathSoFar}.{propName}";
                currentType = resolved.Type;
            }
        }
    }

    private static IPropertySymbol? FindProperty(ITypeSymbol type, string name)
    {
        // Walk the type hierarchy (including base types)
        var current = type;
        while (current is not null)
        {
            foreach (var member in current.GetMembers(name))
            {
                if (member is IPropertySymbol prop && prop.DeclaredAccessibility >= Accessibility.Internal)
                    return prop;
            }
            current = current.BaseType;
        }
        return null;
    }
}
