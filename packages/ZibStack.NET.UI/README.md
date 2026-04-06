# ZibStack.NET.UI

Source generator for **UI form and table metadata**. Annotate your models and get compile-time form descriptors, table column definitions, and JSON schemas — no reflection, no runtime overhead.

## Features

- **Form metadata** — field types, labels, placeholders, groups, ordering, validation, conditional visibility
- **Table metadata** — columns, sorting, filtering, pagination, formatting
- **UI control hints** — `[Select]`, `[Slider]`, `[TextArea]`, `[DatePicker]`, `[PasswordInput]`, `[FilePicker]`, `[RichText]`, and more
- **Framework-agnostic** — generates neutral C# objects + JSON schema consumable by any UI (Blazor, React, Vue, Angular)
- **Cross-package integration** — auto-detects `ZibStack.NET.Validation` and `ZibStack.NET.Dto` attributes
- **Zero reflection** — everything generated at compile time

## Quick Start

```csharp
[Form]
[Table(DefaultSort = "Name")]
[FormGroup("basic", Label = "Basic Info", Order = 1)]
public partial class Player
{
    [FormIgnore]
    public int Id { get; set; }

    [FormField(Label = "Player Name", Placeholder = "Enter name...", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [Slider(Min = 1, Max = 100)]
    [TableColumn(Sortable = true)]
    public int Level { get; set; }

    [Select(typeof(PlayerRole))]
    [TableColumn(Sortable = true, Filterable = true)]
    public PlayerRole Role { get; set; }

    [TextArea(Rows = 3)]
    [TableIgnore]
    public string? Biography { get; set; }

    [PasswordInput]
    [TableIgnore]
    public required string Password { get; set; }

    [FormConditional("Role", "Admin")]
    public string? AdminNotes { get; set; }
}
```

The generator produces:

```csharp
// On the partial class:
Player.GetFormDescriptor()    // FormDescriptor object
Player.GetFormSchemaJson()    // Compile-time baked JSON string
Player.GetTableDescriptor()   // TableDescriptor object
Player.GetTableSchemaJson()   // Compile-time baked JSON string
```

Serve via API:

```csharp
app.MapGet("/api/forms/player", () =>
    Results.Content(Player.GetFormSchemaJson(), "application/json"));

app.MapGet("/api/tables/player", () =>
    Results.Content(Player.GetTableSchemaJson(), "application/json"));
```

## Form Attributes

| Attribute | Target | Purpose |
|-----------|--------|---------|
| `[Form]` | Class | Mark for form generation |
| `[FormGroup("name")]` | Class | Define field group (AllowMultiple) |
| `[FormField]` | Property | Label, Placeholder, HelpText, Order, Group |
| `[FormIgnore]` | Property | Exclude from form |
| `[FormHidden]` | Property | In data but not rendered |
| `[FormOrder(n)]` | Property | Explicit ordering |
| `[FormReadOnly]` | Property | Read-only field |
| `[FormDisabled]` | Property | Disabled field |
| `[FormSection("group")]` | Property | Assign to group |
| `[FormConditional("field", "value")]` | Property | Conditional visibility |

## UI Control Hints

| Attribute | Control |
|-----------|---------|
| `[TextArea(Rows = 3)]` | Multi-line text |
| `[Select(typeof(Enum))]` | Dropdown |
| `[RadioGroup(typeof(Enum))]` | Radio buttons |
| `[Checkbox]` | Toggle |
| `[DatePicker]` | Date selector |
| `[TimePicker]` | Time selector |
| `[DateTimePicker]` | Date + time |
| `[FilePicker(Accept = "image/*")]` | File upload |
| `[ColorPicker]` | Color selector |
| `[RichText]` | Rich text editor |
| `[Slider(Min = 0, Max = 100)]` | Range slider |
| `[PasswordInput]` | Masked input |

## Table Attributes

| Attribute | Target | Purpose |
|-----------|--------|---------|
| `[Table]` | Class | Mark for table generation (DefaultPageSize, PageSizes, DefaultSort) |
| `[TableColumn]` | Property | Sortable, Filterable, Format, Width, IsVisible |
| `[TableIgnore]` | Property | Exclude from table |

## Default Behavior

- All public properties included unless `[FormIgnore]` / `[TableIgnore]`
- UI hint auto-detected from C# type: `string` → text, `bool` → checkbox, `enum` → select, `DateTime` → datePicker
- Labels humanized from property names: `FirstName` → "First Name"
- A class can have both `[Form]` and `[Table]`

## Cross-Package Integration

When `ZibStack.NET.Validation` is referenced, validation attributes (`[Required]`, `[Email]`, `[Range]`, etc.) are automatically included in form metadata.

When `ZibStack.NET.Dto` is referenced, `[CreateOnly]` and `[UpdateOnly]` flags appear in form field descriptors.

No project-level dependencies — detection is by attribute FQN at compile time.
