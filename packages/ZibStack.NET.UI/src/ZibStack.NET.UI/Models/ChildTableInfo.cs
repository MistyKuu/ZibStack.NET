namespace ZibStack.NET.UI;

internal sealed class ChildTableInfo
{
    public string TargetTypeName { get; }
    public string ForeignKey { get; }
    public string Label { get; }
    public string? SchemaUrl { get; }

    public ChildTableInfo(string targetTypeName, string foreignKey, string label, string? schemaUrl)
    {
        TargetTypeName = targetTypeName;
        ForeignKey = foreignKey;
        Label = label;
        SchemaUrl = schemaUrl;
    }
}
