using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Aop.Generator;

public static class AopParser
{
    private const string AspectAttributeFullName = "ZibStack.NET.Aop.AspectAttribute";
    private const string AspectHandlerAttributeFullName = "ZibStack.NET.Aop.AspectHandlerAttribute";
    private const string SensitiveAttributeName = "ZibStack.NET.Aop.SensitiveAttribute";
    private const string LegacySensitiveAttributeName = "ZibStack.NET.Log.SensitiveAttribute";
    private const string NoLogAttributeName = "ZibStack.NET.Aop.NoLogAttribute";
    private const string LegacyNoLogAttributeName = "ZibStack.NET.Log.NoLogAttribute";

    /// <summary>FullyQualifiedFormat + nullable annotations (shows ? on type args, arrays, etc.)</summary>
    private static readonly SymbolDisplayFormat NullableFullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly SymbolDisplayFormat NullableMinimalFormat =
        SymbolDisplayFormat.MinimallyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Checks if an invocation calls a method that has any AspectAttribute-derived attribute.
    /// Returns call-site info if so.
    /// </summary>
    public static CallSiteModel? ParseCallSite(GeneratorSyntaxContext context, CancellationToken ct)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
            return null;

        // Skip our own generated output (always ends in `.g.cs`) so we don't try to
        // intercept calls inside the interceptor wrappers themselves. Empty FilePath is
        // legitimate (in-memory test compilations, scripting, generators that don't set
        // it) — those should still be intercepted, otherwise the integration tests
        // silently no-op and "no codegen" looks indistinguishable from "no errors".
        var filePath = invocation.SyntaxTree.FilePath ?? "";
        if (filePath.EndsWith(".g.cs"))
            return null;

        ct.ThrowIfCancellationRequested();

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, ct);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return null;

        // Check if method or its class has any attribute deriving from AspectAttribute
        bool hasAspect = methodSymbol.GetAttributes()
            .Any(a => DerivesFromAspectAttribute(a.AttributeClass));
        if (!hasAspect && methodSymbol.ContainingType != null)
            hasAspect = methodSymbol.ContainingType.GetAttributes()
                .Any(a => DerivesFromAspectAttribute(a.AttributeClass));

        // Also check if any Apply() rule from IAopConfigurator matches this method
        if (!hasAspect)
        {
            var applyRules = AopConfiguratorParser.ReadAll(context.SemanticModel.Compilation).ApplyRules;
            hasAspect = applyRules.Any(r => MatchesApplyRule(r, methodSymbol));
        }

        // Interface-dispatch: a consumer calling through an interface reference sees
        // ContainingType = interface, which often has no [aspect] attribute even when the
        // concrete impl class carries [Log] at class level. We provisionally accept any
        // invocation whose containing type is a source-declared interface; the downstream
        // Combine step filters by existing class/interface-proxy models, so extra call-sites
        // here produce zero extra codegen — only a matching (synthesized) interface model
        // causes an interceptor to be emitted.
        if (!hasAspect
            && methodSymbol.ContainingType is { TypeKind: TypeKind.Interface } ifaceType
            && ifaceType.Locations.Any(l => l.IsInSource))
        {
            hasAspect = true;
        }

        if (!hasAspect)
            return null;

        var interceptableLocation = context.SemanticModel.GetInterceptableLocation(invocation, ct);
        if (interceptableLocation is null)
            return null;

        var containingType = methodSymbol.ContainingType;
        if (containingType is null)
            return null;

        var containingNs = containingType.ContainingNamespace.IsGlobalNamespace
            ? ""
            : containingType.ContainingNamespace.ToDisplayString();

        // Use OriginalDefinition so generic interface calls (IE2eCanHandle<E2eCreateOrder>.Handle)
        // produce "TCommand" matching the open-generic proxy, not "E2eCreateOrder" which would mismatch.
        var origMethod = methodSymbol.OriginalDefinition;
        var paramSig = string.Join(",", origMethod.Parameters.Select(p =>
            p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

        return new CallSiteModel(
            interceptableLocation.GetInterceptsLocationAttributeSyntax(),
            methodSymbol.Name,
            containingNs,
            containingType.Name,
            containingType.TypeParameters.Length,
            paramSig);
    }

    /// <summary>
    /// Parses a method symbol to extract its intercepted model with all aspect info.
    /// </summary>
    public static InterceptedMethodModel? ParseMethod(IMethodSymbol method, CancellationToken ct)
    {
        if (method.IsStatic)
            return null;

        // Only ordinary methods — skip constructors, finalizers, property/event accessors,
        // operators, conversions, etc. Their CLR names contain characters that aren't valid
        // in C# identifiers (e.g. '.ctor'), and aspects on them rarely make sense anyway.
        if (method.MethodKind != MethodKind.Ordinary)
            return null;

        // Skip compiler-generated methods (record <Clone>$, anonymous types, etc.)
        if (method.Name.Contains("<") || method.Name.Contains(">"))
            return null;

        var aspects = new List<AspectInfo>();
        var seenAspectTypes = new HashSet<string>();

        // Method-level attributes (take priority)
        CollectAspects(method.GetAttributes(), method, aspects, seenAspectTypes);

        // Class-level attributes (apply to all instance methods that an interceptor can
        // actually call back into — public, internal, and protected internal). Private and
        // protected are excluded because the generated interceptor lives in a separate
        // `__X_Aop` class and cannot satisfy private/protected access rules.
        if (method.ContainingType != null && IsInterceptableAccessibility(method.DeclaredAccessibility))
            CollectAspects(method.ContainingType.GetAttributes(), method, aspects, seenAspectTypes);

        // Apply() rules from IAopConfigurator — adds virtual aspects for matching methods.
        // Skip for interface members: they get aspects through ParseInterfaceProxy() which
        // matches rules against the IMPL class. Without this guard, interfaces in a matching
        // namespace get both a direct class model AND a proxy model, causing CS9153.
        if (method.ContainingType?.TypeKind != TypeKind.Interface
            && (method.ContainingAssembly as ISourceAssemblySymbol)?.Compilation is { } compilation)
        {
            var config = AopConfiguratorParser.ReadAll(compilation);
            foreach (var rule in config.ApplyRules)
            {
                if (!seenAspectTypes.Contains(rule.AspectFqn) && MatchesApplyRule(rule, method))
                    CollectVirtualAspect(rule, method, aspects, seenAspectTypes, compilation);
            }
        }

        if (aspects.Count == 0)
            return null;

        aspects.Sort((a, b) => a.Order.CompareTo(b.Order));

        return BuildMethodModel(method, aspects);
    }

    /// <summary>
    /// Builds the parameter/return-type parts of the method model given a pre-resolved
    /// set of aspects. Factored out so <see cref="ParseInterfaceProxy"/> can reuse it.
    /// </summary>
    /// <param name="attributeOverlay">
    /// When non-null, parameter and return-type attributes from this method are merged
    /// with <paramref name="method"/>'s own. Used by <see cref="ParseInterfaceProxy"/>:
    /// the emitted signature has to use the interface's parameter list, but [NoLog] /
    /// [Sensitive] / [return: NoLog] are typically declared on the concrete impl — so
    /// we need to see attributes from both sides or the proxy logs data the impl
    /// marked as redacted.
    /// </param>
    private static InterceptedMethodModel? BuildMethodModel(
        IMethodSymbol method,
        List<AspectInfo> aspects,
        IMethodSymbol? attributeOverlay = null)
    {
        // Parse parameters
        var parameters = new List<InterceptedParameterModel>();
        for (int pi = 0; pi < method.Parameters.Length; pi++)
        {
            var param = method.Parameters[pi];
            var overlayParam = attributeOverlay is not null && pi < attributeOverlay.Parameters.Length
                ? attributeOverlay.Parameters[pi]
                : null;

            bool isSensitive = HasAttr(param, SensitiveAttributeName) || HasAttr(param, LegacySensitiveAttributeName)
                || (overlayParam is not null && (HasAttr(overlayParam, SensitiveAttributeName) || HasAttr(overlayParam, LegacySensitiveAttributeName)));
            bool isNoLog = HasAttr(param, NoLogAttributeName) || HasAttr(param, LegacyNoLogAttributeName)
                || (overlayParam is not null && (HasAttr(overlayParam, NoLogAttributeName) || HasAttr(overlayParam, LegacyNoLogAttributeName)));
            bool isComplex = IsComplexType(param.Type);

            SanitizedTypeModel? sanitizedType = null;
            if (isComplex && !isNoLog && !isSensitive)
                sanitizedType = ParseTypeProperties(param.Type);

            var fqType = param.Type.ToDisplayString(NullableFullyQualifiedFormat);
            var minType = param.Type.ToDisplayString(NullableMinimalFormat);
            // Top-level nullable annotation (param itself marked ?)
            if (param.NullableAnnotation == NullableAnnotation.Annotated
                && !fqType.EndsWith("?"))
            {
                fqType += "?";
                minType += "?";
            }

            parameters.Add(new InterceptedParameterModel(
                param.Name,
                minType,
                fqType,
                isSensitive,
                isNoLog,
                isComplex,
                sanitizedType));
        }

        // Return type (with nullable annotations preserved)
        var returnType = method.ReturnType;
        bool isAsync = false;
        bool returnsVoid = returnType.SpecialType == SpecialType.System_Void;
        string returnTypeStr = returnType.ToDisplayString(NullableFullyQualifiedFormat);
        if (method.ReturnNullableAnnotation == NullableAnnotation.Annotated
            && !returnTypeStr.EndsWith("?"))
            returnTypeStr += "?";

        if (returnType is INamedTypeSymbol namedReturn)
        {
            var fullName = namedReturn.ConstructedFrom.ToDisplayString();
            isAsync = fullName is "System.Threading.Tasks.Task"
                or "System.Threading.Tasks.Task<TResult>"
                or "System.Threading.Tasks.ValueTask"
                or "System.Threading.Tasks.ValueTask<TResult>";

            if (isAsync && !namedReturn.IsGenericType)
                returnsVoid = true;
        }

        // Skip methods whose interceptor body cannot legally call the target. The generated
        // interceptor lives in `__X_Aop` (a separate class) and invokes `@this.Method(...)`,
        // so it needs at-least-internal access to the target. Private and protected fail.
        if (!IsInterceptableAccessibility(method.DeclaredAccessibility) &&
            method.DeclaredAccessibility != Accessibility.NotApplicable)
            return null;

        // Interface members historically report Accessibility.NotApplicable via Roslyn even though
        // they are implicitly public; emit 'public' in that case so the generated extension method
        // signature stays valid.
        var accessibility = method.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.Private => "private",
            _ => "public"
        };

        // Return type complexity + sanitization
        var actualReturnType = returnType;
        if (isAsync && returnType is INamedTypeSymbol asyncRet && asyncRet.IsGenericType)
            actualReturnType = asyncRet.TypeArguments[0];
        bool hasComplexReturnType = !returnsVoid && IsComplexType(actualReturnType);
        SanitizedTypeModel? sanitizedReturnType = null;
        if (hasComplexReturnType)
            sanitizedReturnType = ParseTypeProperties(actualReturnType);

        // Method-level type parameters (distinct from the enclosing class's).
        var methodTypeParameters = ExtractMethodTypeParameters(method);

        // First CancellationToken parameter (if any) — emitter swaps a linked-token
        // CTS in its place so handlers can signal cooperative cancellation that the
        // method body actually observes through its own awaits.
        int? ctIndex = null;
        for (int i = 0; i < method.Parameters.Length; i++)
        {
            if (method.Parameters[i].Type.ToDisplayString() == "System.Threading.CancellationToken")
            {
                ctIndex = i;
                break;
            }
        }

        return new InterceptedMethodModel(
            method.Name,
            returnTypeStr,
            isAsync,
            returnsVoid,
            accessibility,
            parameters,
            aspects,
            hasComplexReturnType,
            sanitizedReturnType,
            methodTypeParameters,
            ctIndex);
    }

    private static IReadOnlyList<TypeParameterModel> ExtractMethodTypeParameters(IMethodSymbol method)
    {
        if (method.TypeParameters.Length == 0)
            return System.Array.Empty<TypeParameterModel>();

        var result = new List<TypeParameterModel>(method.TypeParameters.Length);
        foreach (var tp in method.TypeParameters)
        {
            var constraints = new List<string>();
            if (tp.HasReferenceTypeConstraint)
                constraints.Add(tp.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            else if (tp.HasValueTypeConstraint && !tp.HasUnmanagedTypeConstraint)
                constraints.Add("struct");
            else if (tp.HasUnmanagedTypeConstraint)
                constraints.Add("unmanaged");
            else if (tp.HasNotNullConstraint)
                constraints.Add("notnull");

            foreach (var ct in tp.ConstraintTypes)
                constraints.Add(ct.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

            if (tp.HasConstructorConstraint)
                constraints.Add("new()");

            result.Add(new TypeParameterModel(tp.Name, constraints));
        }
        return result;
    }

    /// <summary>
    /// Finds all methods with aspect attributes in a class, and builds a class model.
    /// Called from ForAttributeWithMetadataName — but we check for any AspectAttribute-derived.
    /// </summary>
    public static InterceptedClassModel? ParseClass(
        INamedTypeSymbol classSymbol,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? classData,
        CancellationToken ct)
    {
        var methods = new List<InterceptedMethodModel>();

        foreach (var member in classSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol methodSymbol)
                continue;

            var model = ParseMethod(methodSymbol, ct);
            if (model != null)
                methods.Add(model);
        }

        if (methods.Count == 0)
            return null;

        var ns = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : classSymbol.ContainingNamespace.ToDisplayString();

        bool isPartial = classSymbol.DeclaringSyntaxReferences
            .Any(r => r.GetSyntax(ct) is ClassDeclarationSyntax cds
                && cds.Modifiers.Any(SyntaxKind.PartialKeyword));

        var typeParameters = ExtractTypeParameters(classSymbol);

        return new InterceptedClassModel(
            ns,
            classSymbol.Name,
            methods,
            classData,
            isPartial,
            typeParameters);
    }

    /// <summary>
    /// Synthesizes an interface class model so that call-sites going through an interface
    /// reference (e.g. via DI) still get intercepted when a concrete implementation class
    /// carries an aspect (class-level <c>[Log]</c> on the impl, OR method-level on the
    /// specific impl method).
    ///
    /// The returned model uses the interface's name/namespace (so call-site matching works)
    /// but its methods inherit whichever aspects apply: interface-member-level, impl-method-
    /// level for the specific implementation, and impl-class-level.
    /// </summary>
    /// <param name="interfaceOpen">
    /// Open-generic form of the interface (e.g. <c>IRepo&lt;T&gt;</c>). Used for emission so the
    /// extension method reproduces the interface's own type parameters. Must equal
    /// <paramref name="interfaceClosed"/>.OriginalDefinition or an already-open symbol.
    /// </param>
    /// <param name="interfaceClosed">
    /// Closed form as seen in the impl's interface list (e.g. <c>IRepo&lt;string&gt;</c>). Needed
    /// for <c>FindImplementationForInterfaceMember</c> to resolve the concrete method.
    /// </param>
    public static InterceptedClassModel? ParseInterfaceProxy(
        INamedTypeSymbol interfaceOpen,
        INamedTypeSymbol interfaceClosed,
        INamedTypeSymbol implClassSymbol,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? classData,
        CancellationToken ct)
    {
        if (interfaceOpen.TypeKind != TypeKind.Interface)
            return null;

        var implAttrs = implClassSymbol.GetAttributes();
        var classLevelAspectAttrs = implAttrs
            .Where(a => DerivesFromAspectAttribute(a.AttributeClass))
            .ToImmutableArray();

        var methods = new List<InterceptedMethodModel>();

        // Walk the CLOSED interface's members — those are the symbols stored in
        // implClassSymbol.Interfaces, and the only shape FindImplementationForInterfaceMember
        // can resolve. For code emission we project each back to the OPEN form via
        // .OriginalDefinition so the generated signature keeps `T` instead of `string`.
        foreach (var member in interfaceClosed.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol closedMethod)
                continue;
            if (closedMethod.MethodKind != MethodKind.Ordinary)
                continue;
            if (closedMethod.IsStatic)
                continue;

            var openMethod = (IMethodSymbol)closedMethod.OriginalDefinition;
            var implMethod = implClassSymbol.FindImplementationForInterfaceMember(closedMethod) as IMethodSymbol;

            var aspects = new List<AspectInfo>();
            var seenAspectTypes = new HashSet<string>();

            // Method-level aspects declared on the interface member itself (rare but valid).
            // Passing implMethod as the attribute-overlay lets [return: NoLog]/[return: Sensitive]
            // declared on the concrete impl flow into the proxy too.
            CollectAspects(openMethod.GetAttributes(), openMethod, aspects, seenAspectTypes, implMethod);

            // Method-level aspects on the concrete implementation of THIS interface member.
            // This is what makes `[Log]` on an impl method intercept calls made via the
            // interface reference too.
            if (implMethod is not null)
                CollectAspects(implMethod.GetAttributes(), openMethod, aspects, seenAspectTypes, implMethod);

            // Impl class-level aspects (e.g. `[Log]` on the class) apply to every public method.
            if (classLevelAspectAttrs.Length > 0)
                CollectAspects(classLevelAspectAttrs, openMethod, aspects, seenAspectTypes, implMethod);

            // Apply() rules from IAopConfigurator — match against the IMPL method so that
            // selectors like ClassesWhere(c => c.Name.StartsWith("Order")) resolve against
            // the concrete class, not the interface.
            if (implMethod is not null
                && (implClassSymbol.ContainingAssembly as ISourceAssemblySymbol)?.Compilation is { } compilation)
            {
                var config = AopConfiguratorParser.ReadAll(compilation);
                foreach (var rule in config.ApplyRules)
                {
                    if (!seenAspectTypes.Contains(rule.AspectFqn) && MatchesApplyRule(rule, implMethod))
                        CollectVirtualAspect(rule, openMethod, aspects, seenAspectTypes, compilation, implMethod);
                }
            }

            if (aspects.Count == 0)
                continue;

            aspects.Sort((a, b) => a.Order.CompareTo(b.Order));

            // attributeOverlay = implMethod so [NoLog]/[Sensitive] on the concrete impl's
            // parameters flow through the interface proxy (the proxy otherwise only sees
            // interface-declared parameter attributes).
            var model = BuildMethodModel(openMethod, aspects, implMethod);
            if (model != null)
                methods.Add(model);
        }

        if (methods.Count == 0)
            return null;

        var ns = interfaceOpen.ContainingNamespace.IsGlobalNamespace
            ? ""
            : interfaceOpen.ContainingNamespace.ToDisplayString();

        var typeParameters = ExtractTypeParameters(interfaceOpen);

        return new InterceptedClassModel(
            ns,
            interfaceOpen.Name,
            methods,
            classData,
            isPartial: false,
            typeParameters: typeParameters,
            isInterfaceProxy: true);
    }

    private static IReadOnlyList<TypeParameterModel> ExtractTypeParameters(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeParameters.Length == 0)
            return System.Array.Empty<TypeParameterModel>();

        var result = new List<TypeParameterModel>(typeSymbol.TypeParameters.Length);
        foreach (var tp in typeSymbol.TypeParameters)
        {
            var constraints = new List<string>();
            // Primary constraints — per C# spec they must come first in the `where` clause.
            if (tp.HasReferenceTypeConstraint)
                constraints.Add(tp.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            else if (tp.HasValueTypeConstraint && !tp.HasUnmanagedTypeConstraint)
                constraints.Add("struct");
            else if (tp.HasUnmanagedTypeConstraint)
                constraints.Add("unmanaged");
            else if (tp.HasNotNullConstraint)
                constraints.Add("notnull");

            foreach (var ct in tp.ConstraintTypes)
                constraints.Add(ct.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

            // new() must be last.
            if (tp.HasConstructorConstraint)
                constraints.Add("new()");

            result.Add(new TypeParameterModel(tp.Name, constraints));
        }
        return result;
    }

    private const int MaxPropertyDepth = 5;

    public static SanitizedTypeModel? ParseTypeProperties(ITypeSymbol type, HashSet<string>? visited = null, int depth = 0)
    {
        if (depth >= MaxPropertyDepth || !IsComplexType(type))
            return null;

        var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        visited ??= new HashSet<string>();
        if (!visited.Add(fullName))
            return null;

        var properties = new List<TypePropertyModel>();
        bool hasDecorated = false;

        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            if (prop.IsStatic || prop.IsIndexer || prop.DeclaredAccessibility != Accessibility.Public) continue;
            if (prop.GetMethod is null) continue;

            var attrs = prop.GetAttributes();
            bool isSensitive = attrs.Any(a => a.AttributeClass?.ToDisplayString() is SensitiveAttributeName or LegacySensitiveAttributeName);
            bool isNoLog = attrs.Any(a => a.AttributeClass?.ToDisplayString() is NoLogAttributeName or LegacyNoLogAttributeName);
            bool isComplex = IsComplexType(prop.Type);

            IReadOnlyList<TypePropertyModel>? nestedProps = null;
            if (isComplex && !isSensitive && !isNoLog)
            {
                var nested = ParseTypeProperties(prop.Type, visited, depth + 1);
                if (nested != null) { nestedProps = nested.Properties; hasDecorated = true; }
            }

            if (isSensitive || isNoLog) hasDecorated = true;

            properties.Add(new TypePropertyModel(
                prop.Name,
                prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                isSensitive, isNoLog, isComplex, nestedProps));
        }

        visited.Remove(fullName);
        if (!hasDecorated) return null;

        var safeName = fullName.Replace("global::", "").Replace(".", "_")
            .Replace("<", "_").Replace(">", "_").Replace(",", "_").Replace(" ", "");

        return new SanitizedTypeModel(fullName, safeName, properties);
    }

    /// <summary>Checks whether a method matches an Apply() rule's selectors.</summary>
    internal static bool MatchesApplyRule(AopConfiguratorParser.ApplyRule rule, IMethodSymbol method)
    {
        var type = method.ContainingType;
        if (type is null) return false;

        // Must be interceptable (not private, not static for now)
        if (method.IsStatic) return false;
        if (!IsInterceptableAccessibility(method.DeclaredAccessibility)) return false;
        if (method.MethodKind != MethodKind.Ordinary) return false;

        // Namespace prefix
        if (rule.NamespacePrefix != null)
        {
            var ns = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString();
            if (!ns.StartsWith(rule.NamespacePrefix)) return false;
        }

        // Implementing<T>
        if (rule.ImplementingFqn != null &&
            !type.AllInterfaces.Any(i => i.ToDisplayString() == rule.ImplementingFqn))
            return false;

        // DerivedFrom<T>
        if (rule.DerivedFromFqn != null)
        {
            bool found = false;
            for (var b = type.BaseType; b != null; b = b.BaseType)
                if (b.ToDisplayString() == rule.DerivedFromFqn) { found = true; break; }
            if (!found) return false;
        }

        // Except<T>
        if (rule.ExceptFqns.Count > 0 && rule.ExceptFqns.Contains(type.ToDisplayString()))
            return false;

        // Class predicates
        if (rule.ClassNameStartsWith != null && !type.Name.StartsWith(rule.ClassNameStartsWith))
            return false;
        if (rule.ClassNameEndsWith != null && !type.Name.EndsWith(rule.ClassNameEndsWith))
            return false;
        if (rule.ClassNameContains != null && !type.Name.Contains(rule.ClassNameContains))
            return false;
        if (rule.ClassIsAbstract.HasValue && type.IsAbstract != rule.ClassIsAbstract.Value)
            return false;
        if (rule.ClassIsSealed.HasValue && type.IsSealed != rule.ClassIsSealed.Value)
            return false;

        // Method predicates
        if (rule.MethodIsAsync.HasValue && method.IsAsync != rule.MethodIsAsync.Value)
            return false;
        if (rule.MethodIsPublic.HasValue && (method.DeclaredAccessibility == Accessibility.Public) != rule.MethodIsPublic.Value)
            return false;
        if (rule.MethodIsStatic.HasValue && method.IsStatic != rule.MethodIsStatic.Value)
            return false;
        if (rule.MethodNameStartsWith != null && !method.Name.StartsWith(rule.MethodNameStartsWith))
            return false;
        if (rule.MethodNameEndsWith != null && !method.Name.EndsWith(rule.MethodNameEndsWith))
            return false;
        if (rule.MethodNameContains != null && !method.Name.Contains(rule.MethodNameContains))
            return false;

        return true;
    }

    /// <summary>
    /// Creates an AspectInfo from an Apply rule — equivalent to having the attribute
    /// on the method, but driven from the fluent config.
    /// </summary>
    /// <param name="attributeOverlay">
    /// Additional symbol whose return-type attributes are merged with
    /// <paramref name="method"/>'s. Used by <see cref="ParseInterfaceProxy"/> so that
    /// <c>[return: NoLog]</c> declared on the concrete impl propagates through the
    /// synthesized interface proxy.
    /// </param>
    private static void CollectVirtualAspect(
        AopConfiguratorParser.ApplyRule rule,
        IMethodSymbol method,
        List<AspectInfo> aspects,
        HashSet<string> seenTypes,
        Compilation compilation,
        IMethodSymbol? attributeOverlay = null)
    {
        if (!seenTypes.Add(rule.AspectFqn)) return;

        // Look up the aspect attribute type to find its handler
        var aspectType = compilation.GetTypeByMetadataName(rule.AspectFqn);
        if (aspectType is null) return;

        var props = new Dictionary<string, object?>(rule.Properties);

        // Merge project-wide defaults
        var allDefaults = AopConfiguratorParser.Read(compilation);
        if (allDefaults.TryGetValue(rule.AspectFqn, out var defaults))
            foreach (var kv in defaults)
                if (!props.ContainsKey(kv.Key)) props[kv.Key] = kv.Value;

        string? handlerTypeName = null;
        string? genericAroundTypeArg = null;
        bool isAsyncHandler = false, hasSyncHandler = false, isAroundHandler = false, isAsyncAroundHandler = false;

        foreach (var classAttr in aspectType.GetAttributes())
        {
            if (classAttr.AttributeClass?.ToDisplayString() == AspectHandlerAttributeFullName
                && classAttr.ConstructorArguments.Length > 0
                && classAttr.ConstructorArguments[0].Value is INamedTypeSymbol handlerType)
            {
                if (handlerType.IsGenericType && !handlerType.IsUnboundGenericType)
                    handlerTypeName = handlerType.ToDisplayString();
                else if (handlerType.IsUnboundGenericType)
                {
                    var returnType = method.ReturnType;
                    if (returnType is INamedTypeSymbol { IsGenericType: true } namedReturn
                        && namedReturn.TypeArguments.Length > 0)
                    {
                        genericAroundTypeArg = namedReturn.TypeArguments[0].ToDisplayString();
                        handlerTypeName = handlerType.ConstructedFrom.Construct(namedReturn.TypeArguments[0]).ToDisplayString();
                    }
                    else
                        handlerTypeName = handlerType.OriginalDefinition.ToDisplayString();
                }
                else
                    handlerTypeName = handlerType.ToDisplayString();

                var actualHandler = handlerType.IsUnboundGenericType ? handlerType.OriginalDefinition : handlerType;
                var ifaceNames = new HashSet<string>(actualHandler.AllInterfaces.Select(i => i.OriginalDefinition.ToDisplayString()));
                hasSyncHandler = ifaceNames.Contains("ZibStack.NET.Aop.IAspectHandler");
                isAsyncHandler = ifaceNames.Contains("ZibStack.NET.Aop.IAsyncAspectHandler");
                isAroundHandler = ifaceNames.Contains("ZibStack.NET.Aop.IAroundAspectHandler")
                               || ifaceNames.Contains("ZibStack.NET.Aop.IAroundAspectHandler<T>");
                isAsyncAroundHandler = ifaceNames.Contains("ZibStack.NET.Aop.IAsyncAroundAspectHandler")
                                     || ifaceNames.Contains("ZibStack.NET.Aop.IAsyncAroundAspectHandler<T>");
            }
        }

        var returnAttrs = method.GetReturnTypeAttributes();
        if (attributeOverlay is not null)
            returnAttrs = returnAttrs.AddRange(attributeOverlay.GetReturnTypeAttributes());
        bool sensitiveReturn = returnAttrs.Any(a => a.AttributeClass?.ToDisplayString() is SensitiveAttributeName or LegacySensitiveAttributeName);
        bool noLogReturn = returnAttrs.Any(a => a.AttributeClass?.ToDisplayString() is NoLogAttributeName or LegacyNoLogAttributeName);

        aspects.Add(new AspectInfo(
            rule.AspectFqn,
            order: 0,
            properties: props,
            handlerTypeName: handlerTypeName,
            isAsyncHandler: isAsyncHandler,
            isAroundHandler: isAroundHandler,
            isAsyncAroundHandler: isAsyncAroundHandler,
            sensitiveReturn: sensitiveReturn,
            noLogReturn: noLogReturn,
            genericAroundTypeArg: genericAroundTypeArg,
            hasSyncHandler: hasSyncHandler));
    }

    /// <summary>
    /// Resolution order for aspect properties (highest wins):
    /// <list type="number">
    ///   <item>method-level attribute: <c>[Log(LogParameters=false)]</c></item>
    ///   <item>class-level attribute: <c>[Log]</c> on the class</item>
    ///   <item><c>b.Apply&lt;LogAttribute&gt;(..., a =&gt; a.LogParameters = false)</c></item>
    ///   <item><c>ILogConfigurator.Defaults(d =&gt; d.LogParameters = false)</c></item>
    ///   <item>hard-coded generator default</item>
    /// </list>
    /// This is realised by populating <see cref="AspectInfo.Properties"/> from sources
    /// (1)–(3) (earlier wins) and <c>InterceptedClassModel.AspectClassData</c> from (4).
    /// The <c>P(...)</c> helper in each emitter consults them in the same order.
    /// </summary>
    private static void CollectAspects(
        System.Collections.Immutable.ImmutableArray<AttributeData> attributes,
        IMethodSymbol method,
        List<AspectInfo> aspects,
        HashSet<string> seenTypes,
        IMethodSymbol? attributeOverlay = null)
    {
        foreach (var attr in attributes)
        {
            if (!DerivesFromAspectAttribute(attr.AttributeClass))
                continue;
            var typeName = attr.AttributeClass!.ToDisplayString();
            if (!seenTypes.Add(typeName))
                continue; // already added from method-level

            int order = 0;
            var props = new Dictionary<string, object?>();
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "Order" && namedArg.Value.Value is int o)
                    order = o;
                else if (namedArg.Value.Kind == TypedConstantKind.Array)
                    props[namedArg.Key] = namedArg.Value.Values;
                else
                    props[namedArg.Key] = namedArg.Value.Value;
            }

            // Merge project-wide IAopConfigurator defaults for keys NOT explicitly set on
            // the attribute. Explicit attribute args always win.
            if ((method.ContainingAssembly as ISourceAssemblySymbol)?.Compilation is { } compilation)
            {
                var allDefaults = AopConfiguratorParser.Read(compilation);
                if (allDefaults.TryGetValue(typeName, out var aspectDefaults))
                {
                    foreach (var kv in aspectDefaults)
                    {
                        if (!props.ContainsKey(kv.Key))
                            props[kv.Key] = kv.Value;
                    }
                }
            }

            string? handlerTypeName = null;
            string? genericAroundTypeArg = null;
            bool isAsyncHandler = false, hasSyncHandler = false, isAroundHandler = false, isAsyncAroundHandler = false;
            foreach (var classAttr in attr.AttributeClass.GetAttributes())
            {
                if (classAttr.AttributeClass?.ToDisplayString() == AspectHandlerAttributeFullName
                    && classAttr.ConstructorArguments.Length > 0
                    && classAttr.ConstructorArguments[0].Value is INamedTypeSymbol handlerType)
                {
                    // Open generic handler (e.g. HybridCacheHandler<>) → close with method's return type
                    if (handlerType.IsUnboundGenericType && handlerType.TypeParameters.Length == 1)
                    {
                        var returnType = method.ReturnType;
                        // Unwrap Task<T> / ValueTask<T> for async methods
                        if (returnType is INamedTypeSymbol asyncRet && asyncRet.IsGenericType)
                        {
                            var defName = asyncRet.OriginalDefinition.ToDisplayString();
                            if (defName == "System.Threading.Tasks.Task<TResult>" ||
                                defName == "System.Threading.Tasks.ValueTask<TResult>")
                                returnType = asyncRet.TypeArguments[0];
                        }
                        handlerType = handlerType.OriginalDefinition.Construct(returnType);
                    }

                    handlerTypeName = handlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var ifaces = handlerType.AllInterfaces;
                    var ifaceNames = ifaces.Select(i => i.ToDisplayString()).ToList();
                    hasSyncHandler = ifaceNames.Contains("ZibStack.NET.Aop.IAspectHandler");
                    isAsyncHandler = ifaceNames.Contains("ZibStack.NET.Aop.IAsyncAspectHandler");
                    isAroundHandler = ifaceNames.Contains("ZibStack.NET.Aop.IAroundAspectHandler");
                    isAsyncAroundHandler = ifaceNames.Contains("ZibStack.NET.Aop.IAsyncAroundAspectHandler");

                    // Detect generic around handlers: IAroundAspectHandler<T> / IAsyncAroundAspectHandler<T>
                    foreach (var iface in ifaces)
                    {
                        if (iface.IsGenericType && iface.TypeArguments.Length == 1)
                        {
                            var def = iface.OriginalDefinition.ToDisplayString();
                            if (def == "ZibStack.NET.Aop.IAroundAspectHandler<T>" ||
                                def == "ZibStack.NET.Aop.IAsyncAroundAspectHandler<T>")
                            {
                                genericAroundTypeArg = iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                                isAroundHandler = true;
                                if (def.Contains("Async"))
                                    isAsyncAroundHandler = true;
                            }
                        }
                    }
                }
            }

            var returnAttrs = method.GetReturnTypeAttributes();
            if (attributeOverlay is not null)
                returnAttrs = returnAttrs.AddRange(attributeOverlay.GetReturnTypeAttributes());
            bool sensitiveReturn = returnAttrs.Any(a => a.AttributeClass?.ToDisplayString() is SensitiveAttributeName or LegacySensitiveAttributeName);
            bool noLogReturn = returnAttrs.Any(a => a.AttributeClass?.ToDisplayString() is NoLogAttributeName or LegacyNoLogAttributeName);

            aspects.Add(new AspectInfo(typeName, order, props, handlerTypeName, isAsyncHandler,
                isAroundHandler || isAsyncAroundHandler, isAsyncAroundHandler, sensitiveReturn, noLogReturn,
                genericAroundTypeArg, hasSyncHandler));
        }
    }

    private static bool DerivesFromAspectAttribute(INamedTypeSymbol? type)
    {
        while (type != null)
        {
            if (type.ToDisplayString() == AspectAttributeFullName)
                return true;
            type = type.BaseType;
        }
        return false;
    }

    private static bool HasAttr(IParameterSymbol param, string attrFqn) =>
        param.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attrFqn);

    /// <summary>
    /// Returns true if a generated interceptor — which lives in a separate `__X_Aop` static
    /// class and invokes `@this.Method(...)` — can legally call a method with this
    /// declared accessibility. Private/protected fail because the interceptor is neither
    /// inside the target class nor a derived class. Internal works only same-assembly,
    /// but that's the same constraint generators already operate under.
    /// </summary>
    public static bool IsInterceptableAccessibility(Accessibility accessibility) =>
        accessibility is Accessibility.Public
                      or Accessibility.Internal
                      or Accessibility.ProtectedOrInternal;

    public static bool IsComplexType(ITypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None)
            return false;
        if (type.TypeKind == TypeKind.Enum)
            return false;
        var fullName = type.ToDisplayString();
        return fullName is not (
            "System.DateTime" or "System.DateTimeOffset" or "System.TimeSpan" or
            "System.Guid" or "System.Uri" or "System.DateOnly" or "System.TimeOnly");
    }
}
