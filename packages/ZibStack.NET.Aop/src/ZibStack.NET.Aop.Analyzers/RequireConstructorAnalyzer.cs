using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZibStack.NET.Aop.Analyzers;

/// <summary>
/// Convention-enforcement analyzer (AOP1004): when a base class or interface is
/// decorated with <c>[RequireConstructor(typeof(...))]</c>, every concrete derivative
/// must declare a public instance constructor matching the configured parameter list.
/// Multiple <c>[RequireConstructor]</c> attributes on the same base mean each shape is
/// checked independently — each missing shape is reported separately.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequireConstructorAnalyzer : DiagnosticAnalyzer
{
    private const string RequireConstructorAttributeFullName = "ZibStack.NET.Aop.RequireConstructorAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.MissingRequiredConstructor);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private readonly struct CtorRequirement
    {
        public ImmutableArray<ITypeSymbol> ParameterTypes { get; }
        public INamedTypeSymbol Source { get; }
        public string? Reason { get; }

        public CtorRequirement(ImmutableArray<ITypeSymbol> parameterTypes, INamedTypeSymbol source, string? reason)
        {
            ParameterTypes = parameterTypes;
            Source = source;
            Reason = reason;
        }

        public string SignatureForMessage() =>
            "(" + string.Join(", ", ParameterTypes.Select(t => t.ToDisplayString())) + ")";
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext ctx)
    {
        var type = (INamedTypeSymbol)ctx.Symbol;

        if (type.TypeKind != TypeKind.Class) return;
        if (type.IsAbstract) return;
        if (type.IsStatic) return;
        if (type.IsImplicitlyDeclared) return;

        var requirements = new List<CtorRequirement>();
        for (var t = type.BaseType; t is not null; t = t.BaseType)
            CollectFromType(t, requirements);
        foreach (var iface in type.AllInterfaces)
            CollectFromType(iface, requirements);

        if (requirements.Count == 0) return;

        // Public instance constructors only — anything else can't be reached by a
        // framework activator that's why the rule was declared in the first place.
        var ctors = type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        foreach (var req in requirements)
        {
            if (ctors.Any(c => Matches(c, req))) continue;
            Report(ctx, type, req);
        }
    }

    private static bool Matches(IMethodSymbol ctor, CtorRequirement req)
    {
        if (ctor.Parameters.Length != req.ParameterTypes.Length) return false;
        for (int i = 0; i < req.ParameterTypes.Length; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(ctor.Parameters[i].Type, req.ParameterTypes[i]))
                return false;
        }
        return true;
    }

    private static void CollectFromType(INamedTypeSymbol type, List<CtorRequirement> requirements)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != RequireConstructorAttributeFullName) continue;

            // ctor takes `params Type[] parameterTypes` — value is a TypedConstant array.
            // Empty array means "require parameterless ctor".
            ImmutableArray<ITypeSymbol> paramTypes;
            if (attr.ConstructorArguments.Length > 0 && !attr.ConstructorArguments[0].Values.IsDefault)
            {
                paramTypes = attr.ConstructorArguments[0].Values
                    .Select(v => v.Value as ITypeSymbol)
                    .Where(t => t is not null)
                    .Cast<ITypeSymbol>()
                    .ToImmutableArray();
            }
            else
            {
                paramTypes = ImmutableArray<ITypeSymbol>.Empty;
            }

            string? reason = null;
            foreach (var na in attr.NamedArguments)
                if (na.Key == "Reason" && na.Value.Value is string r) reason = r;

            // Deduplicate by (source, signature) so two ancestors declaring the same
            // shape don't produce two diagnostics.
            var newReq = new CtorRequirement(paramTypes, type, reason);
            if (!requirements.Any(r => SequenceEqualSymbols(r.ParameterTypes, newReq.ParameterTypes)
                                     && SymbolEqualityComparer.Default.Equals(r.Source, newReq.Source)))
            {
                requirements.Add(newReq);
            }
        }
    }

    private static bool SequenceEqualSymbols(ImmutableArray<ITypeSymbol> a, ImmutableArray<ITypeSymbol> b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (!SymbolEqualityComparer.Default.Equals(a[i], b[i])) return false;
        return true;
    }

    private static void Report(SymbolAnalysisContext ctx, INamedTypeSymbol derived, CtorRequirement req)
    {
        var loc = derived.Locations.FirstOrDefault(l => l.IsInSource) ?? derived.Locations.FirstOrDefault();
        if (loc is null) return;

        var reasonSuffix = string.IsNullOrEmpty(req.Reason) ? "." : $". Reason: {req.Reason}";

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.MissingRequiredConstructor,
            loc,
            derived.Name,
            req.Source.Name,
            req.SignatureForMessage(),
            reasonSuffix));
    }
}
