# ZibStack.NET.UI

Source generator for UI form and table metadata — annotate your models and get compile-time form descriptors, table columns, and JSON schemas.

## Install

```
dotnet add package ZibStack.NET.UI
```

## Quick Start

```csharp
[UiForm]
[UiTable(DefaultSort = "Name", SchemaUrl = "/api/tables/player")]
[UiFormGroup("basic", Label = "Basic Info", Order = 1)]
public partial class PlayerView
{
    [UiFormIgnore]
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [ZRequired] [ZMinLength(2)]
    [UiFormField(Label = "Name", Placeholder = "Enter name...", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [ZRange(1, 100)]
    [UiFormField(Label = "Level", Group = "basic")]
    [UiTableColumn(Sortable = true)]
    public int Level { get; set; }
}
```

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/ui/](https://mistykuu.github.io/ZibStack.NET/packages/ui/)
