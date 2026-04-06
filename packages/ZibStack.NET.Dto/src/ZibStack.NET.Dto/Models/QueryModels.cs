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
    public bool Sortable { get; }
    public string? DefaultSort { get; }
    public int DefaultSortDirection { get; } // 0 = Asc, 1 = Desc

    public QueryDtoInfo(string className, string? ns, string fullyQualifiedName, string queryName, List<QueryPropertyInfo> properties, bool sortable = false, string? defaultSort = null, int defaultSortDirection = 0)
    {
        ClassName = className;
        Namespace = ns;
        FullyQualifiedName = fullyQualifiedName;
        QueryName = queryName;
        Properties = properties;
        Sortable = sortable;
        DefaultSort = defaultSort;
        DefaultSortDirection = defaultSortDirection;
    }
}
