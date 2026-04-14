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
    private const string SensitiveAttributeName = "ZibStack.NET.Log.SensitiveAttribute";
    private const string NoLogAttributeName = "ZibStack.NET.Log.NoLogAttribute";

    /// <summary>
    /// Checks if an invocation calls a method that has any AspectAttribute-derived attribute.
    /// Returns call-site info if so.
    /// </summary>
    public static CallSiteModel? ParseCallSite(GeneratorSyntaxContext context, CancellationToken ct)
    {
        if (context.Node is not InvocationExpressionSyntax invocation)
            return null;

        var filePath = invocation.SyntaxTree.FilePath;
        if (string.IsNullOrEmpty(filePath) || filePath.EndsWith(".g.cs"))
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

        return new CallSiteModel(
            interceptableLocation.GetInterceptsLocationAttributeSyntax(),
            methodSymbol.Name,
            containingNs,
            containingType.Name);
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

        var aspects = new List<AspectInfo>();
        var seenAspectTypes = new HashSet<string>();

        // Method-level attributes (take priority)
        CollectAspects(method.GetAttributes(), method, aspects, seenAspectTypes);

        // Class-level attributes (apply to all public instance methods)
        if (method.ContainingType != null && method.DeclaredAccessibility == Accessibility.Public)
            CollectAspects(method.ContainingType.GetAttributes(), method, aspects, seenAspectTypes);

        if (aspects.Count == 0)
            return null;

        aspects.Sort((a, b) => a.Order.CompareTo(b.Order));

        return BuildMethodModel(method, aspects);
    }

    /// <summary>
    /// Builds the parameter/return-type parts of the method model given a pre-resolved
    /// set of aspects. Factored out so <see cref="ParseInterfaceProxy"/> can reuse it.
    /// </summary>
    private static InterceptedMethodModel? BuildMethodModel(IMethodSymbol method, List<AspectInfo> aspects)
    {
        // Parse parameters
        var parameters = new List<InterceptedParameterModel>();
        foreach (var param in method.Parameters)
        {
            bool isSensitive = param.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == SensitiveAttributeName);
            bool isNoLog = param.GetAttributes()
                .Any(a => a.AttributeClass?.ToDisplayString() == NoLogAttributeName);
            bool isComplex = IsComplexType(param.Type);

            SanitizedTypeModel? sanitizedType = null;
            if (isComplex && !isNoLog && !isSensitive)
                sanitizedType = ParseTypeProperties(param.Type);

            parameters.Add(new InterceptedParameterModel(
                param.Name,
                param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                param.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                isSensitive,
                isNoLog,
                isComplex,
                sanitizedType));
        }

        // Return type
        var returnType = method.ReturnType;
        bool isAsync = false;
        bool returnsVoid = returnType.SpecialType == SpecialType.System_Void;
        string returnTypeStr = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

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
            methodTypeParameters);
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
    /// carries a class-level aspect (e.g. <c>[Log]</c> on the impl class).
    ///
    /// The returned model uses the interface's name/namespace (so call-site matching works)
    /// but its methods inherit the impl class's class-level aspects.
    /// </summary>
    public static InterceptedClassModel? ParseInterfaceProxy(
        INamedTypeSymbol interfaceSymbol,
        INamedTypeSymbol implClassSymbol,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? classData,
        CancellationToken ct)
    {
        if (interfaceSymbol.TypeKind != TypeKind.Interface)
            return null;

        var implAttrs = implClassSymbol.GetAttributes();
        var classLevelAspectAttrs = implAttrs
            .Where(a => DerivesFromAspectAttribute(a.AttributeClass))
            .ToImmutableArray();
        if (classLevelAspectAttrs.Length == 0)
            return null;

        var methods = new List<InterceptedMethodModel>();

        foreach (var member in interfaceSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is not IMethodSymbol methodSymbol)
                continue;
            if (methodSymbol.MethodKind != MethodKind.Ordinary)
                continue;
            if (methodSymbol.IsStatic)
                continue;

            var aspects = new List<AspectInfo>();
            var seenAspectTypes = new HashSet<string>();

            // Method-level aspects declared on the interface member itself (rare but valid).
            CollectAspects(methodSymbol.GetAttributes(), methodSymbol, aspects, seenAspectTypes);
            // Class-level aspects from the impl are the whole reason this proxy exists.
            CollectAspects(classLevelAspectAttrs, methodSymbol, aspects, seenAspectTypes);

            if (aspects.Count == 0)
                continue;

            aspects.Sort((a, b) => a.Order.CompareTo(b.Order));

            var model = BuildMethodModel(methodSymbol, aspects);
            if (model != null)
                methods.Add(model);
        }

        if (methods.Count == 0)
            return null;

        var ns = interfaceSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : interfaceSymbol.ContainingNamespace.ToDisplayString();

        var typeParameters = ExtractTypeParameters(interfaceSymbol);

        return new InterceptedClassModel(
            ns,
            interfaceSymbol.Name,
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
            bool isSensitive = attrs.Any(a => a.AttributeClass?.ToDisplayString() == SensitiveAttributeName);
            bool isNoLog = attrs.Any(a => a.AttributeClass?.ToDisplayString() == NoLogAttributeName);
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

    private static void CollectAspects(
        System.Collections.Immutable.ImmutableArray<AttributeData> attributes,
        IMethodSymbol method,
        List<AspectInfo> aspects,
        HashSet<string> seenTypes)
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
                else
                    props[namedArg.Key] = namedArg.Value.Value;
            }

            string? handlerTypeName = null;
            string? genericAroundTypeArg = null;
            bool isAsyncHandler = false, isAroundHandler = false, isAsyncAroundHandler = false;
            foreach (var classAttr in attr.AttributeClass.GetAttributes())
            {
                if (classAttr.AttributeClass?.ToDisplayString() == AspectHandlerAttributeFullName
                    && classAttr.ConstructorArguments.Length > 0
                    && classAttr.ConstructorArguments[0].Value is INamedTypeSymbol handlerType)
                {
                    handlerTypeName = handlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var ifaces = handlerType.AllInterfaces;
                    var ifaceNames = ifaces.Select(i => i.ToDisplayString()).ToList();
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
            bool sensitiveReturn = returnAttrs.Any(a => a.AttributeClass?.ToDisplayString() == SensitiveAttributeName);
            bool noLogReturn = returnAttrs.Any(a => a.AttributeClass?.ToDisplayString() == NoLogAttributeName);

            aspects.Add(new AspectInfo(typeName, order, props, handlerTypeName, isAsyncHandler,
                isAroundHandler || isAsyncAroundHandler, isAsyncAroundHandler, sensitiveReturn, noLogReturn,
                genericAroundTypeArg));
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
