using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZibStack.NET.Aop.Analyzers;

/// <summary>
/// Convention-enforcement analyzer (AOP1003): when a base class or interface is decorated
/// with <c>[RequireMethod("X", ReturnType = ..., Parameters = ...)]</c>, every concrete
/// derivative must declare a method with that name (and matching signature, if specified).
///
/// <para>
/// Methods inherited from intermediate base types satisfy the rule. Only methods that are
/// reachable as accessible members on the derivative count — private methods on a base
/// class are not visible on the derivative and don't satisfy a public-style convention.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequireMethodAnalyzer : DiagnosticAnalyzer
{
    private const string RequireMethodAttributeFullName = "ZibStack.NET.Aop.RequireMethodAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.MissingRequiredMethod);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private readonly struct MethodRequirement
    {
        public string Name { get; }
        public ITypeSymbol? ReturnType { get; }
        public ImmutableArray<ITypeSymbol>? Parameters { get; }
        public INamedTypeSymbol Source { get; }
        public string? Reason { get; }

        public MethodRequirement(string name, ITypeSymbol? returnType, ImmutableArray<ITypeSymbol>? parameters, INamedTypeSymbol source, string? reason)
        {
            Name = name;
            ReturnType = returnType;
            Parameters = parameters;
            Source = source;
            Reason = reason;
        }

        public string SignatureForMessage()
        {
            var paramList = Parameters is { } p
                ? "(" + string.Join(", ", p.Select(t => t.ToDisplayString())) + ")"
                : "(...)";
            var returnPart = ReturnType is null ? "" : ReturnType.ToDisplayString() + " ";
            return $"{returnPart}{Name}{paramList}";
        }
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext ctx)
    {
        var type = (INamedTypeSymbol)ctx.Symbol;

        if (type.TypeKind != TypeKind.Class) return;
        if (type.IsAbstract) return;
        if (type.IsStatic) return;
        if (type.IsImplicitlyDeclared) return;

        var requirements = new List<MethodRequirement>();
        CollectRequirements(type, requirements);

        if (requirements.Count == 0) return;

        // Walk the type plus its base chain, collect every accessible method symbol once.
        var candidates = new List<IMethodSymbol>();
        for (var t = type; t is not null; t = t.BaseType)
        {
            foreach (var member in t.GetMembers())
            {
                if (member is IMethodSymbol m && m.MethodKind == MethodKind.Ordinary)
                    candidates.Add(m);
            }
        }

        foreach (var req in requirements)
        {
            if (candidates.Any(m => Matches(m, req))) continue;
            Report(ctx, type, req);
        }
    }

    private static bool Matches(IMethodSymbol method, MethodRequirement req)
    {
        if (method.Name != req.Name) return false;
        if (req.ReturnType is { } rt && !SymbolEqualityComparer.Default.Equals(method.ReturnType, rt))
            return false;
        if (req.Parameters is { } ps)
        {
            if (method.Parameters.Length != ps.Length) return false;
            for (int i = 0; i < ps.Length; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(method.Parameters[i].Type, ps[i]))
                    return false;
            }
        }
        return true;
    }

    private static void CollectRequirements(INamedTypeSymbol type, List<MethodRequirement> requirements)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
            CollectFromType(t, requirements);
        foreach (var iface in type.AllInterfaces)
            CollectFromType(iface, requirements);
    }

    private static void CollectFromType(INamedTypeSymbol type, List<MethodRequirement> requirements)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != RequireMethodAttributeFullName) continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not string name || string.IsNullOrEmpty(name)) continue;

            ITypeSymbol? returnType = null;
            ImmutableArray<ITypeSymbol>? parameters = null;
            string? reason = null;
            foreach (var na in attr.NamedArguments)
            {
                if (na.Key == "ReturnType" && na.Value.Value is INamedTypeSymbol rt)
                    returnType = rt;
                else if (na.Key == "Parameters" && !na.Value.Values.IsDefault)
                    parameters = na.Value.Values
                        .Select(v => v.Value as ITypeSymbol)
                        .Where(v => v is not null)
                        .Cast<ITypeSymbol>()
                        .ToImmutableArray();
                else if (na.Key == "Reason" && na.Value.Value is string r)
                    reason = r;
            }

            // Deduplicate by (name, signature, source). Two ancestors declaring the same
            // requirement should not produce two diagnostics.
            var newReq = new MethodRequirement(name, returnType, parameters, type, reason);
            if (!requirements.Any(r =>
                r.Name == newReq.Name &&
                SymbolEqualityComparer.Default.Equals(r.ReturnType, newReq.ReturnType) &&
                ParametersEqual(r.Parameters, newReq.Parameters)))
            {
                requirements.Add(newReq);
            }
        }
    }

    private static bool ParametersEqual(ImmutableArray<ITypeSymbol>? a, ImmutableArray<ITypeSymbol>? b)
    {
        if (!a.HasValue && !b.HasValue) return true;
        if (!a.HasValue || !b.HasValue) return false;
        if (a.Value.Length != b.Value.Length) return false;
        for (int i = 0; i < a.Value.Length; i++)
            if (!SymbolEqualityComparer.Default.Equals(a.Value[i], b.Value[i])) return false;
        return true;
    }

    private static void Report(SymbolAnalysisContext ctx, INamedTypeSymbol derived, MethodRequirement req)
    {
        var loc = derived.Locations.FirstOrDefault(l => l.IsInSource) ?? derived.Locations.FirstOrDefault();
        if (loc is null) return;

        var reasonSuffix = string.IsNullOrEmpty(req.Reason) ? "." : $". Reason: {req.Reason}";

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.MissingRequiredMethod,
            loc,
            derived.Name,
            req.Source.Name,
            req.SignatureForMessage(),
            reasonSuffix));
    }
}
