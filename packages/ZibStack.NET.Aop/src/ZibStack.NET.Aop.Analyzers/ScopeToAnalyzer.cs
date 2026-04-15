using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZibStack.NET.Aop.Analyzers;

/// <summary>
/// Convention-enforcement analyzer (AOP1005): when a type is decorated with
/// <c>[ScopeTo("NS")]</c>, references to it from outside the configured namespace(s)
/// are flagged. Walks <see cref="IObjectCreationOperation"/> (constructor calls) and
/// <see cref="IInvocationOperation"/> (method calls — covers both instance and static
/// member access) and checks the call-site's containing namespace against the rule's
/// pattern.
///
/// <para>
/// Pattern syntax matches <see cref="ZibStack.NET.Aop.ScopeToAttribute"/>: an exact
/// namespace string, or a prefix with <c>".**"</c> suffix to also match all
/// sub-namespaces. Multiple <c>[ScopeTo]</c> attributes mean "any of these is fine".
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ScopeToAnalyzer : DiagnosticAnalyzer
{
    private const string ScopeToAttributeFullName = "ZibStack.NET.Aop.ScopeToAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.OutOfScopeUsage);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext ctx)
    {
        var op = (IObjectCreationOperation)ctx.Operation;
        var targetType = op.Type as INamedTypeSymbol;
        if (targetType is null) return;
        CheckScope(ctx, targetType, op.Syntax.GetLocation());
    }

    private static void AnalyzeInvocation(OperationAnalysisContext ctx)
    {
        var op = (IInvocationOperation)ctx.Operation;
        var targetType = op.TargetMethod.ContainingType;
        if (targetType is null) return;
        CheckScope(ctx, targetType, op.Syntax.GetLocation());
    }

    private static void CheckScope(OperationAnalysisContext ctx, INamedTypeSymbol targetType, Location callSiteLocation)
    {
        var rules = CollectScopeRules(targetType);
        if (rules.Count == 0) return;

        var callSiteNamespace = ResolveCallSiteNamespace(ctx);
        if (callSiteNamespace is null) return;   // unknown — don't speculate

        // Self-reference inside the same containing-type chain is always allowed —
        // the type can talk to itself regardless of its declared scope.
        if (IsInsideTypeChain(ctx.ContainingSymbol, targetType)) return;

        foreach (var (pattern, _) in rules)
        {
            if (Matches(pattern, callSiteNamespace)) return;
        }

        // None matched — pick the first rule's text for the message (most-prominent).
        var first = rules[0];
        var allowed = string.Join(" or ", rules.Select(r => $"'{r.Pattern}'"));
        var reasonSuffix = string.IsNullOrEmpty(first.Reason) ? "." : $". Reason: {first.Reason}";

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.OutOfScopeUsage,
            callSiteLocation,
            targetType.Name,
            allowed,
            callSiteNamespace,
            reasonSuffix));
    }

    private static List<(string Pattern, string? Reason)> CollectScopeRules(INamedTypeSymbol type)
    {
        var rules = new List<(string Pattern, string? Reason)>();
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != ScopeToAttributeFullName) continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not string ns || string.IsNullOrEmpty(ns)) continue;

            string? reason = null;
            foreach (var na in attr.NamedArguments)
                if (na.Key == "Reason" && na.Value.Value is string r) reason = r;

            rules.Add((ns, reason));
        }
        return rules;
    }

    private static bool Matches(string pattern, string callSiteNs)
    {
        if (pattern.EndsWith(".**", System.StringComparison.Ordinal))
        {
            var prefix = pattern.Substring(0, pattern.Length - 3);
            return callSiteNs == prefix
                || callSiteNs.StartsWith(prefix + ".", System.StringComparison.Ordinal);
        }
        return callSiteNs == pattern;
    }

    private static string? ResolveCallSiteNamespace(OperationAnalysisContext ctx)
    {
        // Prefer the type containing the call. Fall back to the symbol's own namespace
        // for top-level statements / global functions where ContainingType is null.
        var containingType = ctx.ContainingSymbol?.ContainingType;
        var ns = containingType?.ContainingNamespace
              ?? ctx.ContainingSymbol?.ContainingNamespace;
        if (ns is null) return null;
        return ns.IsGlobalNamespace ? "" : ns.ToDisplayString();
    }

    private static bool IsInsideTypeChain(ISymbol? containingSymbol, INamedTypeSymbol target)
    {
        for (var t = containingSymbol?.ContainingType; t is not null; t = t.ContainingType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, target))
                return true;
        }
        return false;
    }
}
