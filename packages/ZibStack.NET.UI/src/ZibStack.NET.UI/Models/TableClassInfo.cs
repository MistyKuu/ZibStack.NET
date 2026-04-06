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
        string defaultSortDirection)
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

    public TableColumnInfo(string propertyName, string jsonName, string typeName, string columnType)
    {
        PropertyName = propertyName;
        JsonName = jsonName;
        TypeName = typeName;
        ColumnType = columnType;
    }
}
