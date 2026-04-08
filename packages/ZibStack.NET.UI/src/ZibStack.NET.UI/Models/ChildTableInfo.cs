namespace ZibStack.NET.UI;

internal enum RelationKind
{
    OneToMany,
    OneToOne,
}

internal sealed class RelationInfo
{
    public RelationKind Kind { get; }
    public string TargetTypeName { get; }
    public string PropertyName { get; }
    public string ForeignKey { get; }
    public string Label { get; }
    public string? SchemaUrl { get; }
    public string? FormSchemaUrl { get; }

    public RelationInfo(
        RelationKind kind,
        string targetTypeName,
        string propertyName,
        string foreignKey,
        string label,
        string? schemaUrl,
        string? formSchemaUrl = null)
    {
        Kind = kind;
        TargetTypeName = targetTypeName;
        PropertyName = propertyName;
        ForeignKey = foreignKey;
        Label = label;
        SchemaUrl = schemaUrl;
        FormSchemaUrl = formSchemaUrl;
    }
}
