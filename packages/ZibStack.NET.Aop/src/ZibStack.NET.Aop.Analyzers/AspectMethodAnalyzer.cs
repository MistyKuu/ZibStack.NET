using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZibStack.NET.Aop.Analyzers;

/// <summary>
/// Validates that a method carrying an aspect (any subclass of AspectAttribute, applied
/// directly or via class-level inheritance) is actually interceptable by the generator.
/// Catches the placements that would silently no-op at runtime: static, private/protected,
/// ref/out/in parameters, ref returns, operators.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AspectMethodAnalyzer : DiagnosticAnalyzer
{
    private const string AspectAttributeFullName = "ZibStack.NET.Aop.AspectAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            Diagnostics.StaticMethod,
            Diagnostics.PrivateOrProtected,
            Diagnostics.RefOutInParam,
            Diagnostics.RefReturn,
            Diagnostics.OperatorMethod);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext ctx)
    {
        var method = (IMethodSymbol)ctx.Symbol;

        // Find any aspect attribute on the method itself or its containing type.
        // Container-level aspects only apply to public/internal/protected internal members
        // (matches AopParser.IsInterceptableAccessibility), so do not warn for class-level
        // attribute on a private member — that's "intentionally not intercepted".
        var methodAspect = FindAspectAttribute(method.GetAttributes());
        var classAspect = method.ContainingType is { } ct
            ? FindAspectAttribute(ct.GetAttributes())
            : null;

        // Class-level pickup excludes private/protected members in the parser, so the only
        // way a private/protected method is "claimed" by an aspect is via a method-level
        // attribute. That's the case worth flagging.
        var aspect = methodAspect;
        if (aspect is null && classAspect is not null && IsClassLevelTarget(method))
            aspect = classAspect;

        if (aspect is null)
            return;

        var aspectName = aspect.AttributeClass?.Name ?? "Aspect";

        // Operator / conversion methods: ordinary-only is what the parser emits for.
        if (method.MethodKind != MethodKind.Ordinary)
        {
            // Only flag operators/conversions explicitly — constructors etc. already can't
            // carry attributes derived from AspectAttribute (AttributeUsage doesn't allow it).
            if (method.MethodKind is MethodKind.UserDefinedOperator
                                  or MethodKind.Conversion)
            {
                Report(ctx, Diagnostics.OperatorMethod, method, aspectName, method.Name);
            }
            return;
        }

        if (method.IsStatic)
        {
            Report(ctx, Diagnostics.StaticMethod, method, aspectName, method.Name);
            return;
        }

        if (methodAspect is not null && !IsInterceptableAccessibility(method.DeclaredAccessibility))
        {
            Report(ctx, Diagnostics.PrivateOrProtected, method, aspectName, method.Name);
        }

        if (method.Parameters.Any(p => p.RefKind != RefKind.None))
        {
            Report(ctx, Diagnostics.RefOutInParam, method, aspectName, method.Name);
        }

        if (method.ReturnsByRef || method.ReturnsByRefReadonly)
        {
            Report(ctx, Diagnostics.RefReturn, method, aspectName, method.Name);
        }
    }

    /// <summary>
    /// Returns true if class-level aspects on <paramref name="method"/>'s containing type
    /// would actually be applied to it by the parser. Mirrors AopParser.IsInterceptableAccessibility.
    /// </summary>
    private static bool IsClassLevelTarget(IMethodSymbol method) =>
        !method.IsStatic && IsInterceptableAccessibility(method.DeclaredAccessibility);

    private static bool IsInterceptableAccessibility(Accessibility accessibility) =>
        accessibility is Accessibility.Public
                      or Accessibility.Internal
                      or Accessibility.ProtectedOrInternal
                      // NotApplicable = interface members (implicitly public).
                      or Accessibility.NotApplicable;

    private static AttributeData? FindAspectAttribute(IEnumerable<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            if (DerivesFromAspectAttribute(attr.AttributeClass))
                return attr;
        }
        return null;
    }

    private static bool DerivesFromAspectAttribute(INamedTypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == AspectAttributeFullName)
                return true;
        }
        return false;
    }

    private static void Report(SymbolAnalysisContext ctx, DiagnosticDescriptor rule, ISymbol symbol, params object[] args)
    {
        var loc = symbol.Locations.FirstOrDefault(l => l.IsInSource) ?? symbol.Locations.FirstOrDefault();
        if (loc is null) return;
        ctx.ReportDiagnostic(Diagnostic.Create(rule, loc, args));
    }
}
