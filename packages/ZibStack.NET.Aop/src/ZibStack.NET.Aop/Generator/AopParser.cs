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

        return new InterceptedMethodModel(
            method.Name,
            returnTypeStr,
            isAsync,
            returnsVoid,
            accessibility,
            parameters,
            aspects,
            hasComplexReturnType,
            sanitizedReturnType);
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

        return new InterceptedClassModel(ns, classSymbol.Name, methods, classData);
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
