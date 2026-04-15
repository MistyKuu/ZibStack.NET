using System.Collections.Generic;

namespace ZibStack.NET.Dto;

internal sealed class ResponsePropertyInfo
{
    public string PropertyName { get; }
    /// <summary>Original entity property name (differs from PropertyName when [RenameProperty] is used).</summary>
    public string SourcePropertyName { get; }
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

    public ResponsePropertyInfo(string propertyName, string jsonName, string displayTypeName, List<string>? validationAttributes = null, bool isNestedResponse = false, bool isNullable = false, string? sourceTypeName = null, string? nestedResponseName = null, string? flattenSource = null, string? flattenProjection = null, string? sourcePropertyName = null)
    {
        PropertyName = propertyName;
        SourcePropertyName = sourcePropertyName ?? propertyName;
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
    public string? ListResponseName { get; }
    public List<ResponsePropertyInfo>? ListProperties { get; }

    /// <summary>Pipeline-stamped flag; see <see cref="DtoClassInfo.HasTypeGen"/>.</summary>
    public bool HasTypeGen { get; set; }

    public ResponseDtoInfo(string className, string? ns, string fullyQualifiedName, string responseName, List<ResponsePropertyInfo> properties, string? listResponseName = null, List<ResponsePropertyInfo>? listProperties = null)
    {
        ClassName = className;
        Namespace = ns;
        FullyQualifiedName = fullyQualifiedName;
        ResponseName = responseName;
        Properties = properties;
        ListResponseName = listResponseName;
        ListProperties = listProperties;
    }
}
