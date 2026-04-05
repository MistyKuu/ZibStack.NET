using System;
using System.Collections.Generic;
using System.Linq;

namespace ZibStack.NET.Log.Generator;

internal sealed class ClassModel : IEquatable<ClassModel>
{
    public string Namespace { get; }
    public string ClassName { get; }
    public string LoggerFieldName { get; }
    public string LoggerFieldType { get; }
    public IReadOnlyList<MethodModel> Methods { get; }

    public ClassModel(
        string @namespace,
        string className,
        string loggerFieldName,
        string loggerFieldType,
        IReadOnlyList<MethodModel> methods)
    {
        Namespace = @namespace;
        ClassName = className;
        LoggerFieldName = loggerFieldName;
        LoggerFieldType = loggerFieldType;
        Methods = methods;
    }

    public bool Equals(ClassModel? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Namespace == other.Namespace
            && ClassName == other.ClassName
            && LoggerFieldName == other.LoggerFieldName
            && LoggerFieldType == other.LoggerFieldType
            && Methods.SequenceEqual(other.Methods);
    }

    public override bool Equals(object? obj) => Equals(obj as ClassModel);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + Namespace.GetHashCode();
            hash = hash * 31 + ClassName.GetHashCode();
            hash = hash * 31 + LoggerFieldName.GetHashCode();
            hash = hash * 31 + Methods.Count.GetHashCode();
            return hash;
        }
    }
}

internal sealed class MethodModel : IEquatable<MethodModel>
{
    public string MethodName { get; }
    public string ReturnType { get; }
    public bool IsAsync { get; }
    public bool ReturnsVoid { get; }
    public string Accessibility { get; }
    public IReadOnlyList<ParameterModel> Parameters { get; }
    public int EntryExitLevel { get; }
    public int ExceptionLevel { get; }
    public bool LogParameters { get; }
    public bool LogReturnValue { get; }
    public bool MeasureElapsed { get; }
    public string? EntryMessage { get; }
    public string? ExitMessage { get; }
    public string? ExceptionMessage { get; }
    public int ObjectLogging { get; }
    public bool HasComplexReturnType { get; }
    public bool SensitiveReturn { get; }
    public bool NoLogReturn { get; }
    public SanitizedTypeModel? SanitizedReturnType { get; }

    public MethodModel(
        string methodName,
        string returnType,
        bool isAsync,
        bool returnsVoid,
        string accessibility,
        IReadOnlyList<ParameterModel> parameters,
        int logLevel,
        int onExceptionLevel,
        bool logParameters,
        bool logReturnValue,
        bool measureElapsed,
        string? entryMessage,
        string? exitMessage,
        string? exceptionMessage,
        int objectLogging,
        bool hasComplexReturnType,
        bool sensitiveReturn,
        bool noLogReturn,
        SanitizedTypeModel? sanitizedReturnType = null)
    {
        MethodName = methodName;
        ReturnType = returnType;
        IsAsync = isAsync;
        ReturnsVoid = returnsVoid;
        Accessibility = accessibility;
        Parameters = parameters;
        EntryExitLevel = logLevel;
        ExceptionLevel = onExceptionLevel;
        LogParameters = logParameters;
        LogReturnValue = logReturnValue;
        MeasureElapsed = measureElapsed;
        EntryMessage = entryMessage;
        ExitMessage = exitMessage;
        ExceptionMessage = exceptionMessage;
        ObjectLogging = objectLogging;
        HasComplexReturnType = hasComplexReturnType;
        SensitiveReturn = sensitiveReturn;
        NoLogReturn = noLogReturn;
        SanitizedReturnType = sanitizedReturnType;
    }

    public bool Equals(MethodModel? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return MethodName == other.MethodName
            && ReturnType == other.ReturnType
            && IsAsync == other.IsAsync
            && ReturnsVoid == other.ReturnsVoid
            && Accessibility == other.Accessibility
            && EntryExitLevel == other.EntryExitLevel
            && ExceptionLevel == other.ExceptionLevel
            && LogParameters == other.LogParameters
            && LogReturnValue == other.LogReturnValue
            && MeasureElapsed == other.MeasureElapsed
            && EntryMessage == other.EntryMessage
            && ExitMessage == other.ExitMessage
            && ExceptionMessage == other.ExceptionMessage
            && ObjectLogging == other.ObjectLogging
            && HasComplexReturnType == other.HasComplexReturnType
            && SensitiveReturn == other.SensitiveReturn
            && NoLogReturn == other.NoLogReturn
            && Parameters.SequenceEqual(other.Parameters);
    }

    public override bool Equals(object? obj) => Equals(obj as MethodModel);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + MethodName.GetHashCode();
            hash = hash * 31 + ReturnType.GetHashCode();
            hash = hash * 31 + IsAsync.GetHashCode();
            hash = hash * 31 + Parameters.Count.GetHashCode();
            return hash;
        }
    }
}

internal sealed class ParameterModel : IEquatable<ParameterModel>
{
    public string Name { get; }
    public string Type { get; }
    public string FullyQualifiedType { get; }
    public bool IsNoLog { get; }
    public bool IsSensitive { get; }
    public bool IsComplexType { get; }
    public SanitizedTypeModel? SanitizedType { get; }

    public ParameterModel(string name, string type, string fullyQualifiedType, bool isNoLog, bool isSensitive, bool isComplexType, SanitizedTypeModel? sanitizedType = null)
    {
        Name = name;
        Type = type;
        FullyQualifiedType = fullyQualifiedType;
        IsNoLog = isNoLog;
        IsSensitive = isSensitive;
        IsComplexType = isComplexType;
        SanitizedType = sanitizedType;
    }

    public bool Equals(ParameterModel? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name
            && Type == other.Type
            && FullyQualifiedType == other.FullyQualifiedType
            && IsNoLog == other.IsNoLog
            && IsSensitive == other.IsSensitive
            && IsComplexType == other.IsComplexType;
    }

    public override bool Equals(object? obj) => Equals(obj as ParameterModel);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + Name.GetHashCode();
            hash = hash * 31 + Type.GetHashCode();
            hash = hash * 31 + IsNoLog.GetHashCode();
            hash = hash * 31 + IsSensitive.GetHashCode();
            return hash;
        }
    }
}

internal sealed class CallSiteModel : IEquatable<CallSiteModel>
{
    public string InterceptsLocationAttributeSyntax { get; }
    public string MethodName { get; }
    public string ContainingClassNamespace { get; }
    public string ContainingClassName { get; }

    public CallSiteModel(
        string interceptsLocationAttributeSyntax,
        string methodName,
        string containingClassNamespace,
        string containingClassName)
    {
        InterceptsLocationAttributeSyntax = interceptsLocationAttributeSyntax;
        MethodName = methodName;
        ContainingClassNamespace = containingClassNamespace;
        ContainingClassName = containingClassName;
    }

    public bool Equals(CallSiteModel? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return InterceptsLocationAttributeSyntax == other.InterceptsLocationAttributeSyntax
            && MethodName == other.MethodName
            && ContainingClassName == other.ContainingClassName;
    }

    public override bool Equals(object? obj) => Equals(obj as CallSiteModel);
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + InterceptsLocationAttributeSyntax.GetHashCode();
            return hash;
        }
    }
}
