# ZibStack.NET.UI

Source generator for UI form and table metadata — annotate your models and get compile-time form descriptors, table columns, and JSON schemas.

## Install

```
dotnet add package ZibStack.NET.UI
```

## Quick Start

```csharp
[Form]
[Table(DefaultSort = "Name", SchemaUrl = "/api/tables/player")]
[FormGroup("basic", Label = "Basic Info", Order = 1)]
public partial class PlayerView
{
    [FormIgnore]
    [TableColumn(IsVisible = false)]
    public int Id { get; set; }

    [Required] [MinLength(2)]
    [FormField(Label = "Name", Placeholder = "Enter name...", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [Range(1, 100)]
    [FormField(Label = "Level", Group = "basic")]
    [TableColumn(Sortable = true)]
    public int Level { get; set; }
}
```

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/ui/](https://mistykuu.github.io/ZibStack.NET/packages/ui/)
