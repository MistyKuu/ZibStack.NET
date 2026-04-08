using System.Collections.Generic;

namespace ZibStack.NET.UI;

internal sealed class EntityClassInfo
{
    public string ClassName { get; }
    public string? Namespace { get; }
    public string HintName { get; }
    public string FullyQualifiedName { get; }
    public bool IsRecord { get; }
    public string? TableName { get; }
    public string? Schema { get; }
    public string? PrimaryKeyProperty { get; }
    public List<EntityRelationInfo> Relations { get; }
    public List<string> ComputedProperties { get; }
    public List<string> NavigationProperties { get; }

    public EntityClassInfo(
        string className,
        string? ns,
        string hintName,
        string fullyQualifiedName,
        bool isRecord,
        string? tableName,
        string? schema,
        string? primaryKeyProperty,
        List<EntityRelationInfo> relations,
        List<string> computedProperties,
        List<string> navigationProperties)
    {
        ClassName = className;
        Namespace = ns;
        HintName = hintName;
        FullyQualifiedName = fullyQualifiedName;
        IsRecord = isRecord;
        TableName = tableName;
        Schema = schema;
        PrimaryKeyProperty = primaryKeyProperty;
        Relations = relations;
        ComputedProperties = computedProperties;
        NavigationProperties = navigationProperties;
    }
}

internal sealed class EntityRelationInfo
{
    public RelationKind Kind { get; }
    public string TargetTypeName { get; }
    public string TargetFullyQualifiedName { get; }
    public string PropertyName { get; }
    public string? ForeignKeyProperty { get; }

    public EntityRelationInfo(
        RelationKind kind,
        string targetTypeName,
        string targetFullyQualifiedName,
        string propertyName,
        string? foreignKeyProperty)
    {
        Kind = kind;
        TargetTypeName = targetTypeName;
        TargetFullyQualifiedName = targetFullyQualifiedName;
        PropertyName = propertyName;
        ForeignKeyProperty = foreignKeyProperty;
    }
}
