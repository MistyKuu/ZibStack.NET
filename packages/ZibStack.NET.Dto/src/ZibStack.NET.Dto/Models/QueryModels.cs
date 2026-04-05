using System.Collections.Generic;

namespace ZibStack.NET.Dto;

internal sealed class QueryPropertyInfo
{
    public string PropertyName { get; }
    public string JsonName { get; }
    public string OriginalTypeName { get; }
    public string NullableTypeName { get; }
    public bool IsValueType { get; }

    public QueryPropertyInfo(string propertyName, string jsonName, string originalTypeName, string nullableTypeName, bool isValueType)
    {
        PropertyName = propertyName;
        JsonName = jsonName;
        OriginalTypeName = originalTypeName;
        NullableTypeName = nullableTypeName;
        IsValueType = isValueType;
    }
}

internal sealed class QueryDtoInfo
{
    public string ClassName { get; }
    public string? Namespace { get; }
    public string FullyQualifiedName { get; }
    public string QueryName { get; }
    public List<QueryPropertyInfo> Properties { get; }

    public QueryDtoInfo(string className, string? ns, string fullyQualifiedName, string queryName, List<QueryPropertyInfo> properties)
    {
        ClassName = className;
        Namespace = ns;
        FullyQualifiedName = fullyQualifiedName;
        QueryName = queryName;
        Properties = properties;
    }
}
