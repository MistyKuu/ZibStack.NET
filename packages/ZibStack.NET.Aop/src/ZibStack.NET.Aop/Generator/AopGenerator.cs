using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
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
                    if (ctx.Node is MethodDeclarationSyntax mds)
                    {
                        if (ctx.SemanticModel.GetDeclaredSymbol(mds, ct) is not IMethodSymbol methodSymbol) return null;
                        bool hasAspect = methodSymbol.GetAttributes()
                            .Any(a => DerivesFromAspectAttribute(a.AttributeClass));
                        if (!hasAspect)
                        {
                            var rules = AopConfiguratorParser.ReadAll(ctx.SemanticModel.Compilation).ApplyRules;
                            hasAspect = rules.Any(r => AopParser.MatchesApplyRule(r, methodSymbol));
                        }
                        return hasAspect ? methodSymbol.ContainingType : null;
                    }
                    else if (ctx.Node is ClassDeclarationSyntax cds)
                    {
                        if (ctx.SemanticModel.GetDeclaredSymbol(cds, ct) is not INamedTypeSymbol classSymbol) return null;
                        bool hasAspect = classSymbol.GetAttributes()
                            .Any(a => DerivesFromAspectAttribute(a.AttributeClass));
                        if (!hasAspect)
                        {
                            var rules = AopConfiguratorParser.ReadAll(ctx.SemanticModel.Compilation).ApplyRules;
                            hasAspect = classSymbol.GetMembers().OfType<IMethodSymbol>()
                                .Any(m => rules.Any(r => AopParser.MatchesApplyRule(r, m)));
                        }
                        return hasAspect ? classSymbol : null;
                    }
                    else
                    {
                        return null;
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

        // Step 3: Parse classes with class-level data from providers, plus synthesize
        // interface proxies so calls dispatched through interfaces (typical DI shape) hit
        // the aspect even when [Log] is applied at class level on the implementation.
        var emittersCopy = emitters;
        var classModels = classSymbols
            .Collect()
            .SelectMany((symbols, ct) =>
            {
                var models = new List<InterceptedClassModel>();
                // Interface FQN → owning class FQN, for deterministic first-wins conflict
                // resolution when multiple impls with class-level aspects share an interface.
                var interfaceOwner = new Dictionary<string, string>();

                // Process classes in a deterministic order to make the "first impl wins"
                // rule reproducible across generator runs.
                var ordered = symbols
                    .OrderBy(s => s.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), System.StringComparer.Ordinal)
                    .ToList();

                foreach (var classSymbol in ordered)
                {
                    ct.ThrowIfCancellationRequested();

                    // Collect class data from all providers
                    var classData = new Dictionary<string, IReadOnlyDictionary<string, object?>>();
                    foreach (var provider in providersCopy)
                    {
                        var data = provider.ExtractClassData(classSymbol);
                        if (data != null)
                            classData[provider.AttributeFullName] = data;
                    }

                    var model = AopParser.ParseClass(classSymbol, classData.Count > 0 ? classData : null, ct);
                    model = FilterModelForGenerator(model, emittersCopy);
                    if (model != null)
                        models.Add(model);

                    // Propagate to interfaces whenever the class has ANY aspect — class-level,
                    // method-level, or via Apply() rules. Without this, calls dispatched
                    // through an interface reference silently bypass the aspect in DI scenarios.
                    if (classSymbol.TypeKind != TypeKind.Class)
                        continue;

                    bool hasAnyAspect = classSymbol.GetAttributes()
                        .Any(a => DerivesFromAspectAttribute(a.AttributeClass));
                    if (!hasAnyAspect)
                        hasAnyAspect = classSymbol.GetMembers()
                            .OfType<IMethodSymbol>()
                            .Any(m => m.GetAttributes().Any(a => DerivesFromAspectAttribute(a.AttributeClass)));
                    if (!hasAnyAspect)
                    {
                        if ((classSymbol.ContainingAssembly as ISourceAssemblySymbol)?.Compilation is { } comp)
                        {
                            var rules = AopConfiguratorParser.ReadAll(comp).ApplyRules;
                            hasAnyAspect = classSymbol.GetMembers().OfType<IMethodSymbol>()
                                .Any(m => rules.Any(r => AopParser.MatchesApplyRule(r, m)));
                        }
                    }
                    if (!hasAnyAspect)
                        continue;

                    var classFqn = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    foreach (var iface in classSymbol.AllInterfaces)
                    {
                        // Skip framework/BCL interfaces (IDisposable, IEnumerable, …) — only
                        // propagate to interfaces declared in the user's compilation.
                        if (!iface.Locations.Any(l => l.IsInSource))
                            continue;

                        // Use the ORIGINAL (open-generic) definition so the emitted extension
                        // method reproduces the interface's type parameters verbatim (e.g.
                        // `this IRepo<T>` with `return T?`) instead of a substituted closed
                        // form (`this IRepo<T>` with `return string?` — which the compiler
                        // rightly refuses as CS0029 since __result is typed as T).
                        var ifaceOpen = iface.OriginalDefinition;
                        var ifaceFqn = ifaceOpen.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        if (interfaceOwner.ContainsKey(ifaceFqn))
                            continue; // first impl wins

                        var proxy = AopParser.ParseInterfaceProxy(
                            ifaceOpen, iface, classSymbol,
                            classData.Count > 0 ? classData : null,
                            ct);
                        proxy = FilterModelForGenerator(proxy, emittersCopy);
                        if (proxy != null)
                        {
                            models.Add(proxy);
                            interfaceOwner[ifaceFqn] = classFqn;
                        }
                    }
                }

                return models;
            });

        // Step 4: Combine and emit
        var combined = classModels.Combine(callSiteCollection);

        context.RegisterSourceOutput(combined, (spc, pair) =>
        {
            var (classModel, allCallSites) = pair;

            var relevantCallSites = allCallSites
                .Where(cs => cs.ContainingClassName == classModel.ClassName
                    && cs.ContainingClassNamespace == classModel.Namespace
                    && cs.ContainingTypeArity == classModel.TypeParameters.Count)
                .ToList();

            if (relevantCallSites.Count == 0)
                return;

            var source = AopEmitter.Emit(classModel, relevantCallSites, emittersCopy);
            // Interface proxies use a distinct suffix so they don't collide with a real
            // class named the same as the interface (and so both files can be emitted when
            // a class has class-level [Log] + implements an interface of matching name).
            var arityTag = classModel.TypeParameters.Count > 0
                ? $"_{classModel.TypeParameters.Count}"
                : "";
            var hintName = classModel.IsInterfaceProxy
                ? $"{classModel.ClassName}{arityTag}_IfaceAop.g.cs"
                : $"{classModel.ClassName}{arityTag}_Aop.g.cs";
            spc.AddSource(hintName, source);

            // Emit code map only for partial classes (never for interface proxies — they
            // refer to an interface, not a class, so `partial class` wouldn't compile).
            if (classModel.IsPartial && !classModel.IsInterfaceProxy)
            {
                var codeMap = GenerateAopCodeMap(classModel);
                var codeMapArity = classModel.TypeParameters.Count > 0
                    ? $"_{classModel.TypeParameters.Count}"
                    : "";
                spc.AddSource($"{classModel.ClassName}{codeMapArity}.Aop.CodeMap.g.cs", codeMap);
            }
        });
    }

    private static string GenerateAopCodeMap(InterceptedClassModel classModel)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(classModel.Namespace))
        {
            sb.AppendLine($"namespace {classModel.Namespace};");
            sb.AppendLine();
        }

        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Generated code map for <c>{classModel.ClassName}</c> (AOP):");
        sb.AppendLine("/// <list type=\"bullet\">");
        var codeMapArityTag = classModel.TypeParameters.Count > 0
            ? $"_{classModel.TypeParameters.Count}"
            : "";
        sb.AppendLine($"/// <item><description>AOP wrapper: <see cref=\"ZibStack.Generated.__{classModel.ClassName}{codeMapArityTag}_Aop\"/> — interceptor extension methods</description></item>");
        foreach (var method in classModel.Methods)
        {
            var aspectNames = string.Join(", ", method.Aspects.Select(a =>
            {
                var name = a.AttributeFullName;
                var dot = name.LastIndexOf('.');
                return dot >= 0 ? name.Substring(dot + 1) : name;
            }));
            sb.AppendLine($"/// <item><description>{method.MethodName}() — [{aspectNames}]</description></item>");
        }
        sb.AppendLine("/// </list>");
        sb.AppendLine("/// </summary>");

        // Preserve the original open-generic signature so the partial hook actually matches
        // the user-declared type (`partial class BaseService` would NOT match `BaseService<T>`).
        string classNameWithTypeParams = classModel.ClassName;
        if (classModel.TypeParameters.Count > 0)
        {
            var tpNames = string.Join(", ", classModel.TypeParameters.Select(t => t.Name));
            classNameWithTypeParams = $"{classModel.ClassName}<{tpNames}>";
        }
        sb.AppendLine($"partial class {classNameWithTypeParams} {{ }}");

        return sb.ToString();
    }

    /// <summary>
    /// Filters a class model to only include aspects the current generator can handle:
    /// aspects with a registered emitter OR a runtime handler. Returns null if no methods remain.
    /// </summary>
    private static InterceptedClassModel? FilterModelForGenerator(
        InterceptedClassModel? model,
        IReadOnlyDictionary<string, IAspectEmitter> emitters)
    {
        if (model is null) return null;

        var filteredMethods = new List<InterceptedMethodModel>(model.Methods.Count);
        bool anyChanged = false;

        foreach (var method in model.Methods)
        {
            var kept = method.Aspects
                .Where(a => emitters.ContainsKey(a.AttributeFullName) || a.HandlerTypeName != null)
                .ToList();

            if (kept.Count == 0) { anyChanged = true; continue; }

            if (kept.Count != method.Aspects.Count)
            {
                anyChanged = true;
                filteredMethods.Add(new InterceptedMethodModel(
                    method.MethodName, method.ReturnType, method.IsAsync, method.ReturnsVoid,
                    method.Accessibility, method.Parameters, kept,
                    method.HasComplexReturnType, method.SanitizedReturnType,
                    method.MethodTypeParameters, method.CancellationTokenParameterIndex));
            }
            else
            {
                filteredMethods.Add(method);
            }
        }

        if (filteredMethods.Count == 0) return null;
        if (!anyChanged) return model;

        return new InterceptedClassModel(
            model.Namespace, model.ClassName, filteredMethods,
            model.AspectClassData, model.IsPartial, model.TypeParameters, model.IsInterfaceProxy);
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
