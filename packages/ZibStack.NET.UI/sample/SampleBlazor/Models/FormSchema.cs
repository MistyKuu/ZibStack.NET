namespace SampleBlazor.Models;

/// <summary>
/// Deserialization models for the JSON schema produced by ZibStack.NET.UI.
/// These mirror the JSON structure from GetFormSchemaJson() / GetTableSchemaJson().
/// </summary>
public record FormSchema(
    string Name,
    string Layout,
    List<GroupSchema> Groups,
    List<FieldSchema> Fields);

public record GroupSchema(string Name, string? Label, int Order);

public record FieldSchema(
    string Name,
    string Type,
    string UiHint,
    string? Label,
    string? Placeholder,
    string? HelpText,
    string? Group,
    int Order,
    bool? Required,
    bool? Hidden,
    bool? ReadOnly,
    bool? Disabled,
    bool? CreateOnly,
    bool? UpdateOnly,
    bool? Nullable,
    List<OptionSchema>? Options,
    ConditionalSchema? Conditional,
    Dictionary<string, object>? Validation,
    Dictionary<string, object>? Props);

public record OptionSchema(string Value, string Label);

public record ConditionalSchema(string Field, string Operator, string Value);

public record TableSchema(
    string Name,
    List<ColumnSchema> Columns,
    PaginationSchema Pagination,
    SortSchema? DefaultSort);

public record ColumnSchema(
    string Name,
    string Type,
    string? Label,
    bool Sortable,
    bool Filterable,
    string? Format,
    int Order,
    bool? Visible,
    string? Width,
    List<string>? Options);

public record PaginationSchema(int DefaultPageSize, List<int> PageSizes);

public record SortSchema(string Column, string Direction);
