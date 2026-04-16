using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Aop.Generator;

/// <summary>
/// Extracts project-wide aspect defaults from <c>IAopConfigurator.Configure(IAopBuilder b)</c>.
/// The body is parsed as a compile-time DSL — never invoked at runtime.
///
/// <para>
/// Recognized shape:
/// <code>
/// b.Retry(r =>   { r.MaxAttempts = 5; r.DelayMs = 200; });
/// b.Timeout(t => { t.TimeoutMs = 10_000; });
/// </code>
/// Each section name maps to an attribute FQN; each property assigned inside the lambda
/// contributes one entry to that aspect's defaults dictionary. Anything Roslyn can treat
/// as a compile-time constant (literals, enum members, const fields, casts, unary ops)
/// is accepted.
/// </para>
/// </summary>
public static class AopConfiguratorParser
{
    private const string ConfiguratorInterfaceFqn = "ZibStack.NET.Aop.IAopConfigurator";

    // Section name (matching IAopBuilder method) → aspect attribute FQN.
    private static readonly Dictionary<string, string> SectionToAspect = new()
    {
        ["Retry"] = "ZibStack.NET.Aop.RetryAttribute",
        ["Timeout"] = "ZibStack.NET.Aop.TimeoutAttribute",
        ["Trace"] = "ZibStack.NET.Aop.TraceAttribute",
        ["Cache"] = "ZibStack.NET.Aop.CacheAttribute",
        ["Metrics"] = "ZibStack.NET.Aop.MetricsAttribute",
    };

    // Cache per Compilation — Aop pipeline asks for defaults per aspect collection pass.
    private static readonly ConditionalWeakTable<Compilation, Dictionary<string, IReadOnlyDictionary<string, object?>>> _cache = new();

    /// <summary>
    /// Returns per-aspect defaults keyed by attribute FQN (e.g. <c>"ZibStack.NET.Aop.RetryAttribute" → { "MaxAttempts" = 5, ... }</c>).
    /// Empty dictionary when no configurator is found.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> Read(Compilation compilation)
    {
        if (_cache.TryGetValue(compilation, out var cached)) return cached;
        var result = ReadUncached(compilation);
        _cache.Add(compilation, result);
        return result;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, object?>> ReadUncached(Compilation compilation)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, object?>>();
        var iface = compilation.GetTypeByMetadataName(ConfiguratorInterfaceFqn);
        if (iface is null) return result;

        foreach (var t in EnumerateAllTypes(compilation.Assembly.GlobalNamespace))
        {
            if (!t.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, iface)))
                continue;

            var configure = t.GetMembers("Configure").OfType<IMethodSymbol>().FirstOrDefault();
            if (configure is null) continue;

            var syntaxRef = configure.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef?.GetSyntax() is not MethodDeclarationSyntax method || method.Body is null)
                continue;

            var semanticModel = compilation.GetSemanticModel(method.SyntaxTree);
            ParseBody(method.Body, semanticModel, result);
        }
        return result;
    }

    private static void ParseBody(
        BlockSyntax body,
        SemanticModel sm,
        Dictionary<string, IReadOnlyDictionary<string, object?>> result)
    {
        foreach (var stmt in body.Statements)
        {
            if (stmt is not ExpressionStatementSyntax es) continue;
            if (es.Expression is not InvocationExpressionSyntax inv) continue;
            if (inv.Expression is not MemberAccessExpressionSyntax mae) continue;
            var sectionName = mae.Name.Identifier.Text;
            if (!SectionToAspect.TryGetValue(sectionName, out var aspectFqn)) continue;
            if (inv.ArgumentList.Arguments.Count != 1) continue;
            if (inv.ArgumentList.Arguments[0].Expression is not SimpleLambdaExpressionSyntax lambda)
                continue;
            if (lambda.Body is not BlockSyntax lambdaBody) continue;

            var sectionDict = new Dictionary<string, object?>();
            if (result.TryGetValue(aspectFqn, out var existing))
                foreach (var kv in existing) sectionDict[kv.Key] = kv.Value;

            foreach (var lstmt in lambdaBody.Statements)
            {
                if (lstmt is not ExpressionStatementSyntax les) continue;
                if (les.Expression is not AssignmentExpressionSyntax assign) continue;
                if (assign.Left is not MemberAccessExpressionSyntax lma) continue;

                var propName = lma.Name.Identifier.Text;
                var constant = sm.GetConstantValue(assign.Right);
                if (!constant.HasValue) continue;

                sectionDict[propName] = constant.Value;
            }
            result[aspectFqn] = sectionDict;
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateAllTypes(INamespaceSymbol ns)
    {
        foreach (var t in ns.GetTypeMembers())
        {
            yield return t;
            foreach (var nested in EnumerateNested(t)) yield return nested;
        }
        foreach (var sub in ns.GetNamespaceMembers())
            foreach (var t in EnumerateAllTypes(sub)) yield return t;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNested(INamedTypeSymbol t)
    {
        foreach (var n in t.GetTypeMembers())
        {
            yield return n;
            foreach (var nn in EnumerateNested(n)) yield return nn;
        }
    }
}
