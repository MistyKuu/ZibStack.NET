using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Log.Generator;

/// <summary>
/// Extracts project-wide defaults from <c>ILogConfigurator.Configure(ILogBuilder b)</c>
/// method bodies. The body is parsed as a compile-time DSL — never invoked at runtime.
///
/// <para>
/// Recognized shape:
/// <code>
/// b.Defaults(d =>
/// {
///     d.EntryExitLevel = ZibLogLevel.Debug;   // int literal / const / enum member — anything Roslyn sees as a compile-time constant
///     d.MeasureElapsed = false;
/// });
/// b.Interpolation(i => { i.PropertyNameCasing = ZibLogPropertyCasing.CamelCase; });
/// </code>
/// Unknown section names, unknown property names, and non-constant values are silently
/// ignored for now (matches the attribute-era behavior of swallowing unknown fields).
/// </para>
/// </summary>
internal sealed class LogFluentDefaults
{
    public int? EntryExitLevel;
    public int? ExceptionLevel;
    public bool? LogParameters;
    public bool? LogReturnValue;
    public bool? MeasureElapsed;
    public int? ObjectLogging;
    public int? PropertyNameCasing;

    /// <summary>All named defaults as a flat dictionary (matches the old <c>Default_{Key}</c> shape
    /// consumed by <c>LogAspectEmitter.P(...)</c> — keeps the emitter unchanged).</summary>
    public IReadOnlyDictionary<string, object?> ToDefaultsDictionary()
    {
        var d = new Dictionary<string, object?>();
        if (EntryExitLevel.HasValue) d["Default_EntryExitLevel"] = EntryExitLevel.Value;
        if (ExceptionLevel.HasValue) d["Default_ExceptionLevel"] = ExceptionLevel.Value;
        if (LogParameters.HasValue) d["Default_LogParameters"] = LogParameters.Value;
        if (LogReturnValue.HasValue) d["Default_LogReturnValue"] = LogReturnValue.Value;
        if (MeasureElapsed.HasValue) d["Default_MeasureElapsed"] = MeasureElapsed.Value;
        if (ObjectLogging.HasValue) d["Default_ObjectLogging"] = ObjectLogging.Value;
        return d;
    }
}

internal static class LogConfiguratorParser
{
    private const string ConfiguratorInterfaceFqn = "ZibStack.NET.Log.ILogConfigurator";

    // Cache per Compilation — LogClassDataProvider.ExtractClassData runs once per class
    // annotated with [Log], so without caching we'd re-scan every type in the compilation
    // for each class. ConditionalWeakTable ties lifetime to the Compilation instance.
    private static readonly ConditionalWeakTable<Compilation, LogFluentDefaults> _cache = new();

    public static LogFluentDefaults Read(Compilation compilation)
    {
        if (_cache.TryGetValue(compilation, out var cached)) return cached;

        var result = ReadUncached(compilation);
        _cache.Add(compilation, result);
        return result;
    }

    private static LogFluentDefaults ReadUncached(Compilation compilation)
    {
        var result = new LogFluentDefaults();
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

    private static void ParseBody(BlockSyntax body, SemanticModel sm, LogFluentDefaults result)
    {
        foreach (var stmt in body.Statements)
        {
            // b.Defaults(d => { ... })  or  b.Interpolation(i => { ... })
            if (stmt is not ExpressionStatementSyntax es) continue;
            if (es.Expression is not InvocationExpressionSyntax inv) continue;
            if (inv.Expression is not MemberAccessExpressionSyntax mae) continue;
            var sectionName = mae.Name.Identifier.Text;
            if (inv.ArgumentList.Arguments.Count != 1) continue;
            if (inv.ArgumentList.Arguments[0].Expression is not SimpleLambdaExpressionSyntax lambda)
                continue;
            if (lambda.Body is not BlockSyntax lambdaBody) continue;

            foreach (var lstmt in lambdaBody.Statements)
            {
                if (lstmt is not ExpressionStatementSyntax les) continue;
                if (les.Expression is not AssignmentExpressionSyntax assign) continue;
                if (assign.Left is not MemberAccessExpressionSyntax lma) continue;

                var propName = lma.Name.Identifier.Text;
                var constant = sm.GetConstantValue(assign.Right);
                if (!constant.HasValue) continue;

                Set(result, sectionName, propName, constant.Value);
            }
        }
    }

    private static void Set(LogFluentDefaults r, string section, string prop, object? value)
    {
        if (value is null) return;
        switch (section)
        {
            case "Defaults":
                switch (prop)
                {
                    case "EntryExitLevel": r.EntryExitLevel = AsInt(value); break;
                    case "ExceptionLevel": r.ExceptionLevel = AsInt(value); break;
                    case "LogParameters": if (value is bool lp) r.LogParameters = lp; break;
                    case "LogReturnValue": if (value is bool lr) r.LogReturnValue = lr; break;
                    case "MeasureElapsed": if (value is bool me) r.MeasureElapsed = me; break;
                    case "ObjectLogging": r.ObjectLogging = AsInt(value); break;
                }
                break;
            case "Interpolation":
                switch (prop)
                {
                    case "PropertyNameCasing": r.PropertyNameCasing = AsInt(value); break;
                }
                break;
        }
    }

    private static int? AsInt(object? v) => v switch
    {
        int i => i,
        long l => (int)l,
        short s => s,
        byte b => b,
        _ => null,
    };

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
