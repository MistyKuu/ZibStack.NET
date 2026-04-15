using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZibStack.NET.Aop.Analyzers;

/// <summary>
/// Convention-enforcement analyzer (AOP1001): when a base class or interface is decorated
/// with <c>[RequireAspect(typeof(X))]</c>, every concrete derivative must also carry
/// <c>[X]</c>. Reports on the derivative's name with a message that includes the rule's
/// optional <c>Reason</c>, so the developer immediately sees both *what* and *why*.
///
/// <para>
/// Walks the type's full base chain + all implemented interfaces and groups requirements
/// by the required aspect type, so a single missing <c>[Log]</c> never produces multiple
/// duplicate warnings even if both a base and an interface declared the same rule.
/// </para>
///
/// <para>
/// Abstract classes and interfaces are exempt: the rule is meant to fire at concrete
/// usage sites, not on every step of an inheritance chain.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequireAspectAnalyzer : DiagnosticAnalyzer
{
    private const string RequireAspectAttributeFullName = "ZibStack.NET.Aop.RequireAspectAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.MissingRequiredAspect);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext ctx)
    {
        var type = (INamedTypeSymbol)ctx.Symbol;

        // Concrete classes only — interfaces and abstract bases are part of the chain
        // a rule walks UP from a concrete derivative; flagging them on the way down would
        // be double-counting and noisy.
        if (type.TypeKind != TypeKind.Class) return;
        if (type.IsAbstract) return;
        if (type.IsStatic) return;
        // Generated/compiler-synthesized types — don't pile on errors there.
        if (type.IsImplicitlyDeclared) return;

        // Collect all unique aspect requirements from the base chain + all interfaces.
        // Keying by the required aspect type collapses duplicate rules into one diagnostic.
        var requirements = new Dictionary<INamedTypeSymbol, (INamedTypeSymbol Source, string? Reason)>(SymbolEqualityComparer.Default);
        CollectRequirementsFromAncestors(type, requirements);

        if (requirements.Count == 0) return;

        // Aspects already present on the type (or inherited via the attribute's own
        // [AttributeUsage(Inherited = true)] from a parent) satisfy the rule.
        var presentAttributes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass is { } ac)
                presentAttributes.Add(ac);
        }

        foreach (var kvp in requirements)
        {
            var requiredAspect = kvp.Key;
            if (presentAttributes.Contains(requiredAspect)) continue;
            // Also accept derived attributes — if the rule asks for [Log] and the type
            // carries [LogVerbose : Log], that's fine.
            if (presentAttributes.Any(a => InheritsFrom(a, requiredAspect))) continue;

            ReportMissing(ctx, type, kvp.Value.Source, requiredAspect, kvp.Value.Reason);
        }
    }

    private static void CollectRequirementsFromAncestors(
        INamedTypeSymbol type,
        Dictionary<INamedTypeSymbol, (INamedTypeSymbol Source, string? Reason)> requirements)
    {
        // Base class chain.
        for (var t = type.BaseType; t is not null; t = t.BaseType)
            CollectFromType(t, requirements);

        // Every interface (including transitively-implemented ones — AllInterfaces handles that).
        foreach (var iface in type.AllInterfaces)
            CollectFromType(iface, requirements);
    }

    private static void CollectFromType(
        INamedTypeSymbol type,
        Dictionary<INamedTypeSymbol, (INamedTypeSymbol Source, string? Reason)> requirements)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != RequireAspectAttributeFullName) continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol aspectType) continue;

            string? reason = null;
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "Reason" && named.Value.Value is string r)
                    reason = r;
            }

            // First wins — a closer ancestor's rule (and its Reason) takes precedence
            // over one inherited from further up. We walk base first then interfaces,
            // so direct base.Reason wins over an interface's.
            if (!requirements.ContainsKey(aspectType))
                requirements[aspectType] = (type, reason);
        }
    }

    private static bool InheritsFrom(INamedTypeSymbol candidate, INamedTypeSymbol target)
    {
        for (var t = candidate.BaseType; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, target))
                return true;
        }
        return false;
    }

    private static void ReportMissing(
        SymbolAnalysisContext ctx,
        INamedTypeSymbol derived,
        INamedTypeSymbol source,
        INamedTypeSymbol requiredAspect,
        string? reason)
    {
        // Render "[Log]" without the "Attribute" suffix to match how users write attributes.
        var shortAspect = StripAttributeSuffix(requiredAspect.Name);
        var reasonSuffix = string.IsNullOrEmpty(reason) ? "." : $". Reason: {reason}";

        var loc = derived.Locations.FirstOrDefault(l => l.IsInSource) ?? derived.Locations.FirstOrDefault();
        if (loc is null) return;

        // Stash the required aspect's full name in properties so the code-fix can find
        // and re-import it without re-running symbol resolution.
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add("RequiredAspectFullName", requiredAspect.ToDisplayString())
            .Add("RequiredAspectShortName", shortAspect);

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.MissingRequiredAspect,
            loc,
            properties,
            derived.Name,
            source.Name,
            shortAspect,
            reasonSuffix));
    }

    private static string StripAttributeSuffix(string name) =>
        name.EndsWith("Attribute", System.StringComparison.Ordinal)
            ? name.Substring(0, name.Length - "Attribute".Length)
            : name;
}
