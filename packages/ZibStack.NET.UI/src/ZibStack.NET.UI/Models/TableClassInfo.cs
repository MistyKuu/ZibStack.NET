using System.Collections.Generic;

namespace ZibStack.NET.UI;

internal sealed class TableClassInfo
{
    public string ClassName { get; }
    public string? Namespace { get; }
    public string HintName { get; }
    public string TableName { get; }
    public bool IsRecord { get; }
    public List<TableColumnInfo> Columns { get; }
    public int DefaultPageSize { get; }
    public int[] PageSizes { get; }
    public string? DefaultSort { get; }
    public string DefaultSortDirection { get; }
    public string? SchemaUrl { get; }
    public string? ApiUrl { get; set; }
    public string? KeyProperty { get; set; }
    public List<RelationInfo> Relations { get; } = new List<RelationInfo>();
    public List<RowActionInfo> RowActions { get; } = new List<RowActionInfo>();
    public List<ToolbarActionInfo> ToolbarActions { get; } = new List<ToolbarActionInfo>();
    public PermissionInfo? Permissions { get; set; }

    public TableClassInfo(
        string className,
        string? ns,
        string hintName,
        string tableName,
        bool isRecord,
        List<TableColumnInfo> columns,
        int defaultPageSize,
        int[] pageSizes,
        string? defaultSort,
        string defaultSortDirection,
        string? schemaUrl = null)
    {
        ClassName = className;
        Namespace = ns;
        HintName = hintName;
        TableName = tableName;
        IsRecord = isRecord;
        Columns = columns;
        DefaultPageSize = defaultPageSize;
        PageSizes = pageSizes;
        DefaultSort = defaultSort;
        DefaultSortDirection = defaultSortDirection;
        SchemaUrl = schemaUrl;
    }
}

internal sealed class TableColumnInfo
{
    public string PropertyName { get; }
    public string JsonName { get; }
    public string TypeName { get; }
    public string ColumnType { get; }
    public string? Label { get; set; }
    public bool Sortable { get; set; }
    public bool Filterable { get; set; }
    public string? Format { get; set; }
    public int Order { get; set; }
    public bool IsVisible { get; set; } = true;
    public string? Width { get; set; }
    public bool IsEnum { get; set; }
    public List<string> EnumValues { get; } = new List<string>();
    public bool IsComputed { get; set; }
    public List<ColumnStyleInfo> Styles { get; } = new List<ColumnStyleInfo>();

    public TableColumnInfo(string propertyName, string jsonName, string typeName, string columnType)
    {
        PropertyName = propertyName;
        JsonName = jsonName;
        TypeName = typeName;
        ColumnType = columnType;
    }
}
