using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Aop.Generator;

/// <summary>
/// Shared AOP generator infrastructure. Not a [Generator] itself — consuming packages
/// (like ZibStack.NET.Log) create their own IIncrementalGenerator and use this helper
/// to set up the pipeline with their IAspectEmitter registrations.
/// </summary>
public static class AopPipeline
{
    /// <summary>
    /// Registers the full AOP pipeline in a generator's Initialize method.
    /// </summary>
    public static void Register(
        IncrementalGeneratorInitializationContext context,
        IReadOnlyDictionary<string, IAspectEmitter> emitters,
        System.Action<IncrementalGeneratorInitializationContext, IncrementalValuesProvider<INamedTypeSymbol>>? classDiscoveryHook = null)
    {
        // Step 1: Find call-sites to methods with any AspectAttribute-derived attribute
        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InvocationExpressionSyntax,
                transform: static (ctx, ct) => AopParser.ParseCallSite(ctx, ct))
            .Where(static cs => cs is not null)
            .Select(static (cs, _) => cs!);

        var callSiteCollection = callSites.Collect();

        // Step 2: Find classes with aspect-attributed methods
        var classSymbols = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, ct) =>
                {
                    var methodSymbol = ctx.SemanticModel.GetDeclaredSymbol((MethodDeclarationSyntax)ctx.Node, ct);
                    if (methodSymbol is null) return null;

                    bool hasAspect = methodSymbol.GetAttributes()
                        .Any(a => DerivesFromAspectAttribute(a.AttributeClass));
                    return hasAspect ? methodSymbol.ContainingType : null;
                })
            .Where(static t => t is not null)
            .Select(static (t, _) => t!)
            .Collect()
            .SelectMany(static (types, _) => types.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default));

        // Allow consuming generators to hook into class discovery (e.g., to extract logger field)
        classDiscoveryHook?.Invoke(context, classSymbols);

        var classModels = classSymbols
            .Select(static (classSymbol, ct) => AopParser.ParseClass(classSymbol, null, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // Step 3: Combine and emit
        var combined = classModels.Combine(callSiteCollection);

        var emittersCopy = emitters; // capture for lambda
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
