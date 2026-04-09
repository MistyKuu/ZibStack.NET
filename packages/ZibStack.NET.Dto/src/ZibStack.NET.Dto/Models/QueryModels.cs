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

/// <summary>A OneToMany collection navigation for DSL filtering (e.g. "Players.Name" → x.Players.Any(p => p.Name == v)).</summary>
internal sealed class QueryCollectionPath
{
    /// <summary>Dot path for switch key (e.g. "players.name").</summary>
    public string DotPath { get; }
    /// <summary>Collection property name (e.g. "Players").</summary>
    public string CollectionName { get; }
    /// <summary>Collection element type FQN (e.g. "Player").</summary>
    public string ElementTypeName { get; }
    /// <summary>Child property name (e.g. "Name").</summary>
    public string ChildPropertyName { get; }
    /// <summary>Child property type (e.g. "string").</summary>
    public string ChildPropertyTypeName { get; }
    public bool IsValueType { get; }

    public QueryCollectionPath(string dotPath, string collectionName, string elementTypeName, string childPropertyName, string childPropertyTypeName, bool isValueType)
    {
        DotPath = dotPath;
        CollectionName = collectionName;
        ElementTypeName = elementTypeName;
        ChildPropertyName = childPropertyName;
        ChildPropertyTypeName = childPropertyTypeName;
        IsValueType = isValueType;
    }
}

/// <summary>A navigation property path for DSL filtering (e.g. "Team.Name" → x => x.Team.Name).</summary>
internal sealed class QueryNavigationPath
{
    /// <summary>Dot-separated path for the switch key (e.g. "team.name").</summary>
    public string DotPath { get; }
    /// <summary>C# expression path (e.g. "Team.Name").</summary>
    public string ExpressionPath { get; }
    /// <summary>The leaf property's original type name.</summary>
    public string OriginalTypeName { get; }
    public bool IsValueType { get; }

    public QueryNavigationPath(string dotPath, string expressionPath, string originalTypeName, bool isValueType)
    {
        DotPath = dotPath;
        ExpressionPath = expressionPath;
        OriginalTypeName = originalTypeName;
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
    public List<QueryNavigationPath> NavigationPaths { get; }
    public List<QueryCollectionPath> CollectionPaths { get; }
    /// <summary>Top-level navigation property names (e.g. "Team") for Include generation.</summary>
    public List<string> NavigationNames { get; }
    public bool Sortable { get; }
    public string? DefaultSort { get; }
    public int DefaultSortDirection { get; } // 0 = Asc, 1 = Desc
    public bool HasQueryDsl { get; set; }
    public bool HasEfCore { get; set; }

    public QueryDtoInfo(string className, string? ns, string fullyQualifiedName, string queryName, List<QueryPropertyInfo> properties, bool sortable = false, string? defaultSort = null, int defaultSortDirection = 0, List<QueryNavigationPath>? navigationPaths = null, List<string>? navigationNames = null, List<QueryCollectionPath>? collectionPaths = null)
    {
        ClassName = className;
        Namespace = ns;
        FullyQualifiedName = fullyQualifiedName;
        QueryName = queryName;
        Properties = properties;
        NavigationPaths = navigationPaths ?? new List<QueryNavigationPath>();
        CollectionPaths = collectionPaths ?? new List<QueryCollectionPath>();
        NavigationNames = navigationNames ?? new List<string>();
        Sortable = sortable;
        DefaultSort = defaultSort;
        DefaultSortDirection = defaultSortDirection;
    }
}
