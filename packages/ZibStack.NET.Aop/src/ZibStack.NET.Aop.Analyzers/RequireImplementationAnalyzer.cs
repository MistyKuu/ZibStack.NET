using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZibStack.NET.Aop.Analyzers;

/// <summary>
/// Convention-enforcement analyzer (AOP1002): when a base class or interface is
/// decorated with <c>[RequireImplementation(typeof(I))]</c>, every concrete derivative
/// must also implement <c>I</c>. Walks the full base chain plus all interfaces of the
/// derivative type and reports one diagnostic per missing required interface.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequireImplementationAnalyzer : DiagnosticAnalyzer
{
    private const string RequireImplementationAttributeFullName = "ZibStack.NET.Aop.RequireImplementationAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.MissingRequiredImplementation);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext ctx)
    {
        var type = (INamedTypeSymbol)ctx.Symbol;

        // Same scope filter as the other Tier 4 analyzers — concrete classes only.
        if (type.TypeKind != TypeKind.Class) return;
        if (type.IsAbstract) return;
        if (type.IsStatic) return;
        if (type.IsImplicitlyDeclared) return;

        var requirements = new Dictionary<INamedTypeSymbol, (INamedTypeSymbol Source, string? Reason)>(SymbolEqualityComparer.Default);
        CollectRequirements(type, requirements);

        if (requirements.Count == 0) return;

        var implementedInterfaces = new HashSet<INamedTypeSymbol>(type.AllInterfaces, SymbolEqualityComparer.Default);

        foreach (var kvp in requirements)
        {
            var requiredInterface = kvp.Key;
            if (implementedInterfaces.Contains(requiredInterface)) continue;
            // Also accept derived interfaces — implementing IAsyncDisposable should
            // satisfy a [RequireImplementation(typeof(IDisposable))] only if there's an
            // actual inheritance link. Roslyn handles this naturally via AllInterfaces.

            Report(ctx, type, kvp.Value.Source, requiredInterface, kvp.Value.Reason);
        }
    }

    private static void CollectRequirements(
        INamedTypeSymbol type,
        Dictionary<INamedTypeSymbol, (INamedTypeSymbol Source, string? Reason)> requirements)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
            CollectFromType(t, requirements);

        foreach (var iface in type.AllInterfaces)
            CollectFromType(iface, requirements);
    }

    private static void CollectFromType(
        INamedTypeSymbol type,
        Dictionary<INamedTypeSymbol, (INamedTypeSymbol Source, string? Reason)> requirements)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != RequireImplementationAttributeFullName) continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol ifaceType) continue;

            string? reason = null;
            foreach (var named in attr.NamedArguments)
                if (named.Key == "Reason" && named.Value.Value is string r) reason = r;

            if (!requirements.ContainsKey(ifaceType))
                requirements[ifaceType] = (type, reason);
        }
    }

    private static void Report(
        SymbolAnalysisContext ctx,
        INamedTypeSymbol derived,
        INamedTypeSymbol source,
        INamedTypeSymbol requiredInterface,
        string? reason)
    {
        var loc = derived.Locations.FirstOrDefault(l => l.IsInSource) ?? derived.Locations.FirstOrDefault();
        if (loc is null) return;

        var reasonSuffix = string.IsNullOrEmpty(reason) ? "." : $". Reason: {reason}";

        // Stash the interface's full and short name in properties so the code-fix can
        // reuse them without re-running symbol resolution.
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add("RequiredInterfaceFullName", requiredInterface.ToDisplayString())
            .Add("RequiredInterfaceShortName", requiredInterface.Name);

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.MissingRequiredImplementation,
            loc,
            properties,
            derived.Name,
            source.Name,
            requiredInterface.Name,
            reasonSuffix));
    }
}
