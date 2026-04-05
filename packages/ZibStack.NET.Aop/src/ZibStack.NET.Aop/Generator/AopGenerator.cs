using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Aop.Generator;

/// <summary>
/// Provides class-level data for specific aspects (e.g., logger field for [Log]).
/// </summary>
public interface IClassDataProvider
{
    /// <summary>The aspect attribute FQN this provider serves.</summary>
    string AttributeFullName { get; }

    /// <summary>Extract class-level data from the class symbol.</summary>
    IReadOnlyDictionary<string, object?>? ExtractClassData(INamedTypeSymbol classSymbol);

    /// <summary>Report diagnostics for the class.</summary>
    IEnumerable<Diagnostic> GetDiagnostics(INamedTypeSymbol classSymbol);
}

/// <summary>
/// Shared AOP generator infrastructure. Consuming packages create their own
/// IIncrementalGenerator and call AopPipeline.Register() with emitters and class data providers.
/// </summary>
public static class AopPipeline
{
    public static void Register(
        IncrementalGeneratorInitializationContext context,
        IReadOnlyDictionary<string, IAspectEmitter> emitters,
        IReadOnlyList<IClassDataProvider>? classDataProviders = null)
    {
        var providers = classDataProviders ?? System.Array.Empty<IClassDataProvider>();

        // Step 1: Find call-sites
        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InvocationExpressionSyntax,
                transform: static (ctx, ct) => AopParser.ParseCallSite(ctx, ct))
            .Where(static cs => cs is not null)
            .Select(static (cs, _) => cs!);

        var callSiteCollection = callSites.Collect();

        // Step 2: Find classes with aspect-attributed methods
        // Find classes with aspect-attributed methods OR class-level aspect attributes
        var classSymbols = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is MethodDeclarationSyntax or ClassDeclarationSyntax,
                transform: static (ctx, ct) =>
                {
                    if (ctx.Node is MethodDeclarationSyntax)
                    {
                        var methodSymbol = ctx.SemanticModel.GetDeclaredSymbol((MethodDeclarationSyntax)ctx.Node, ct);
                        if (methodSymbol is null) return null;
                        bool hasAspect = methodSymbol.GetAttributes()
                            .Any(a => DerivesFromAspectAttribute(a.AttributeClass));
                        return hasAspect ? methodSymbol.ContainingType : null;
                    }
                    else
                    {
                        var classSymbol = ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node, ct);
                        if (classSymbol is null) return null;
                        bool hasAspect = classSymbol.GetAttributes()
                            .Any(a => DerivesFromAspectAttribute(a.AttributeClass));
                        return hasAspect ? classSymbol : null;
                    }
                })
            .Where(static t => t is not null)
            .Select(static (t, _) => t!)
            .Collect()
            .SelectMany(static (types, _) =>
            {
                var seen = new HashSet<string>();
                var result = new List<INamedTypeSymbol>();
                foreach (var t in types)
                    if (seen.Add(t.ToDisplayString()))
                        result.Add((INamedTypeSymbol)t);
                return result;
            });

        // Step 2b: Report diagnostics from class data providers
        var providersCopy = providers;
        context.RegisterSourceOutput(classSymbols.Collect(), (spc, symbols) =>
        {
            foreach (var cls in symbols)
            {
                foreach (var provider in providersCopy)
                {
                    foreach (var diag in provider.GetDiagnostics(cls))
                        spc.ReportDiagnostic(diag);
                }
            }
        });

        // Step 3: Parse classes with class-level data from providers
        var classModels = classSymbols
            .Select((classSymbol, ct) =>
            {
                // Collect class data from all providers
                var classData = new Dictionary<string, IReadOnlyDictionary<string, object?>>();
                foreach (var provider in providersCopy)
                {
                    var data = provider.ExtractClassData(classSymbol);
                    if (data != null)
                        classData[provider.AttributeFullName] = data;
                }

                return AopParser.ParseClass(classSymbol, classData.Count > 0 ? classData : null, ct);
            })
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // Step 4: Combine and emit
        var combined = classModels.Combine(callSiteCollection);

        var emittersCopy = emitters;
        context.RegisterSourceOutput(combined, (spc, pair) =>
        {
            var (classModel, allCallSites) = pair;

            var relevantCallSites = allCallSites
                .Where(cs => cs.ContainingClassName == classModel.ClassName
                    && cs.ContainingClassNamespace == classModel.Namespace)
                .ToList();

            if (relevantCallSites.Count == 0)
                return;

            var source = AopEmitter.Emit(classModel, relevantCallSites, emittersCopy);
            spc.AddSource($"{classModel.ClassName}_Aop.g.cs", source);
        });
    }

    private static bool DerivesFromAspectAttribute(INamedTypeSymbol? type)
    {
        while (type != null)
        {
            if (type.ToDisplayString() == "ZibStack.NET.Aop.AspectAttribute")
                return true;
            type = type.BaseType;
        }
        return false;
    }
}
