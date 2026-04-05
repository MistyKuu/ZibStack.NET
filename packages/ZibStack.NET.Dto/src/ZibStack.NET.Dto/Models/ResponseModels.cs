using System.Collections.Generic;

namespace ZibStack.NET.Dto;

internal sealed class ResponsePropertyInfo
{
    public string PropertyName { get; }
    public string JsonName { get; }
    public string DisplayTypeName { get; }
    public List<string> ValidationAttributes { get; }
    public bool IsNestedResponse { get; }
    public bool IsNullable { get; }
    public string? SourceTypeName { get; }
    public string? NestedResponseName { get; }
    public string? FlattenSource { get; }
    public string? FlattenProjection { get; }
    public bool IsFlattened => FlattenSource is not null;

    public ResponsePropertyInfo(string propertyName, string jsonName, string displayTypeName, List<string>? validationAttributes = null, bool isNestedResponse = false, bool isNullable = false, string? sourceTypeName = null, string? nestedResponseName = null, string? flattenSource = null, string? flattenProjection = null)
    {
        PropertyName = propertyName;
        JsonName = jsonName;
        DisplayTypeName = displayTypeName;
        ValidationAttributes = validationAttributes ?? new List<string>();
        IsNestedResponse = isNestedResponse;
        IsNullable = isNullable;
        SourceTypeName = sourceTypeName;
        NestedResponseName = nestedResponseName;
        FlattenSource = flattenSource;
        FlattenProjection = flattenProjection;
    }
}

internal sealed class ResponseDtoInfo
{
    public string ClassName { get; }
    public string? Namespace { get; }
    public string FullyQualifiedName { get; }
    public string ResponseName { get; }
    public List<ResponsePropertyInfo> Properties { get; }

    public ResponseDtoInfo(string className, string? ns, string fullyQualifiedName, string responseName, List<ResponsePropertyInfo> properties)
    {
        ClassName = className;
        Namespace = ns;
        FullyQualifiedName = fullyQualifiedName;
        ResponseName = responseName;
        Properties = properties;
    }
}
