using System;
using System.Collections.Generic;
using System.Linq;

namespace ZibStack.NET.Aop.Generator;

public sealed class InterceptedClassModel : IEquatable<InterceptedClassModel>
{
    public string Namespace { get; }
    public string ClassName { get; }
    public IReadOnlyList<InterceptedMethodModel> Methods { get; }

    /// <summary>
    /// Aspect-specific class-level data. Key = aspect attribute FQN, Value = arbitrary data.
    /// E.g., Log stores logger field name/type here.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> AspectClassData { get; }

    public InterceptedClassModel(
        string @namespace,
        string className,
        IReadOnlyList<InterceptedMethodModel> methods,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? aspectClassData = null)
    {
        Namespace = @namespace;
        ClassName = className;
        Methods = methods;
        AspectClassData = aspectClassData ?? new Dictionary<string, IReadOnlyDictionary<string, object?>>();
    }

    public bool Equals(InterceptedClassModel? other)
    {
        if (other is null) return false;
        return Namespace == other.Namespace && ClassName == other.ClassName;
    }

    public override bool Equals(object? obj) => Equals(obj as InterceptedClassModel);
    public override int GetHashCode() => (Namespace, ClassName).GetHashCode();
}

public sealed class InterceptedMethodModel : IEquatable<InterceptedMethodModel>
{
    public string MethodName { get; }
    public string ReturnType { get; }
    public bool IsAsync { get; }
    public bool ReturnsVoid { get; }
    public string Accessibility { get; }
    public IReadOnlyList<InterceptedParameterModel> Parameters { get; }
    public IReadOnlyList<AspectInfo> Aspects { get; }
    public bool HasComplexReturnType { get; }
    public SanitizedTypeModel? SanitizedReturnType { get; }

    public InterceptedMethodModel(
        string methodName,
        string returnType,
        bool isAsync,
        bool returnsVoid,
        string accessibility,
        IReadOnlyList<InterceptedParameterModel> parameters,
        IReadOnlyList<AspectInfo> aspects,
        bool hasComplexReturnType = false,
        SanitizedTypeModel? sanitizedReturnType = null)
    {
        MethodName = methodName;
        ReturnType = returnType;
        IsAsync = isAsync;
        ReturnsVoid = returnsVoid;
        Accessibility = accessibility;
        Parameters = parameters;
        Aspects = aspects;
        HasComplexReturnType = hasComplexReturnType;
        SanitizedReturnType = sanitizedReturnType;
    }

    public bool Equals(InterceptedMethodModel? other)
    {
        if (other is null) return false;
        return MethodName == other.MethodName && ReturnType == other.ReturnType;
    }

    public override bool Equals(object? obj) => Equals(obj as InterceptedMethodModel);
    public override int GetHashCode() => MethodName.GetHashCode();
}

public sealed class InterceptedParameterModel : IEquatable<InterceptedParameterModel>
{
    public string Name { get; }
    public string Type { get; }
    public string FullyQualifiedType { get; }
    public bool IsSensitive { get; }
    public bool IsNoLog { get; }
    public bool IsComplexType { get; }
    public SanitizedTypeModel? SanitizedType { get; }

    public InterceptedParameterModel(string name, string type, string fullyQualifiedType,
        bool isSensitive, bool isNoLog, bool isComplexType, SanitizedTypeModel? sanitizedType = null)
    {
        Name = name;
        Type = type;
        FullyQualifiedType = fullyQualifiedType;
        IsSensitive = isSensitive;
        IsNoLog = isNoLog;
        IsComplexType = isComplexType;
        SanitizedType = sanitizedType;
    }

    public bool Equals(InterceptedParameterModel? other)
    {
        if (other is null) return false;
        return Name == other.Name && FullyQualifiedType == other.FullyQualifiedType;
    }

    public override bool Equals(object? obj) => Equals(obj as InterceptedParameterModel);
    public override int GetHashCode() => Name.GetHashCode();
}

public sealed class AspectInfo : IEquatable<AspectInfo>
{
    public string AttributeFullName { get; }
    public int Order { get; }

    /// <summary>
    /// Named argument values from the attribute. E.g., Level=2, MeasureElapsed=true.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Properties { get; }

    /// <summary>
    /// For [AspectHandler]-based aspects: the fully qualified handler type name.
    /// Null for inline emitter aspects.
    /// </summary>
    public string? HandlerTypeName { get; }

    /// <summary>Whether the handler implements IAsyncAspectHandler (vs IAspectHandler).</summary>
    public bool IsAsyncHandler { get; }

    /// <summary>Whether the handler implements IAroundAspectHandler or IAsyncAroundAspectHandler.</summary>
    public bool IsAroundHandler { get; }

    /// <summary>Whether the around handler is async (IAsyncAroundAspectHandler).</summary>
    public bool IsAsyncAroundHandler { get; }

    /// <summary>
    /// For generic around handlers (IAroundAspectHandler&lt;T&gt; / IAsyncAroundAspectHandler&lt;T&gt;),
    /// the fully qualified type argument. Null for non-generic handlers.
    /// </summary>
    public string? GenericAroundTypeArg { get; }

    /// <summary>
    /// Return-value attributes: [return: Sensitive], [return: NoLog]
    /// </summary>
    public bool SensitiveReturn { get; }
    public bool NoLogReturn { get; }

    public AspectInfo(string attributeFullName, int order,
        IReadOnlyDictionary<string, object?> properties,
        string? handlerTypeName = null,
        bool isAsyncHandler = false,
        bool isAroundHandler = false,
        bool isAsyncAroundHandler = false,
        bool sensitiveReturn = false, bool noLogReturn = false,
        string? genericAroundTypeArg = null)
    {
        AttributeFullName = attributeFullName;
        Order = order;
        Properties = properties;
        HandlerTypeName = handlerTypeName;
        IsAsyncHandler = isAsyncHandler;
        IsAroundHandler = isAroundHandler;
        IsAsyncAroundHandler = isAsyncAroundHandler;
        SensitiveReturn = sensitiveReturn;
        NoLogReturn = noLogReturn;
        GenericAroundTypeArg = genericAroundTypeArg;
    }

    public bool Equals(AspectInfo? other)
    {
        if (other is null) return false;
        return AttributeFullName == other.AttributeFullName && Order == other.Order;
    }

    public override bool Equals(object? obj) => Equals(obj as AspectInfo);
    public override int GetHashCode() => AttributeFullName.GetHashCode();
}

public sealed class CallSiteModel : IEquatable<CallSiteModel>
{
    public string InterceptsLocationAttributeSyntax { get; }
    public string MethodName { get; }
    public string ContainingClassNamespace { get; }
    public string ContainingClassName { get; }

    public CallSiteModel(string interceptsLocationAttributeSyntax, string methodName,
        string containingClassNamespace, string containingClassName)
    {
        InterceptsLocationAttributeSyntax = interceptsLocationAttributeSyntax;
        MethodName = methodName;
        ContainingClassNamespace = containingClassNamespace;
        ContainingClassName = containingClassName;
    }

    public bool Equals(CallSiteModel? other)
    {
        if (other is null) return false;
        return InterceptsLocationAttributeSyntax == other.InterceptsLocationAttributeSyntax;
    }

    public override bool Equals(object? obj) => Equals(obj as CallSiteModel);
    public override int GetHashCode() => InterceptsLocationAttributeSyntax.GetHashCode();
}

/// <summary>
/// A complex type whose properties have [Sensitive]/[NoLog] attributes.
/// Used to generate sanitizer methods for property-level masking.
/// </summary>
public sealed class SanitizedTypeModel
{
    public string FullyQualifiedType { get; }
    public string SafeName { get; }
    public IReadOnlyList<TypePropertyModel> Properties { get; }

    public SanitizedTypeModel(string fullyQualifiedType, string safeName, IReadOnlyList<TypePropertyModel> properties)
    {
        FullyQualifiedType = fullyQualifiedType;
        SafeName = safeName;
        Properties = properties;
    }
}

public sealed class TypePropertyModel
{
    public string Name { get; }
    public string FullyQualifiedType { get; }
    public bool IsSensitive { get; }
    public bool IsNoLog { get; }
    public bool IsComplexType { get; }
    public IReadOnlyList<TypePropertyModel>? NestedProperties { get; }

    public TypePropertyModel(string name, string fullyQualifiedType,
        bool isSensitive, bool isNoLog, bool isComplexType,
        IReadOnlyList<TypePropertyModel>? nestedProperties)
    {
        Name = name;
        FullyQualifiedType = fullyQualifiedType;
        IsSensitive = isSensitive;
        IsNoLog = isNoLog;
        IsComplexType = isComplexType;
        NestedProperties = nestedProperties;
    }
}
