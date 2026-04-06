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

## Generated JSON

### Form Schema

```json
{
  "name": "Player",
  "layout": "vertical",
  "groups": [
    { "name": "basic", "label": "Basic Info", "order": 1 }
  ],
  "fields": [
    {
      "name": "name",
      "type": "string",
      "uiHint": "text",
      "label": "Player Name",
      "placeholder": "Enter name...",
      "group": "basic",
      "order": 0,
      "required": true,
      "validation": { "required": true, "minLength": 2 }
    },
    {
      "name": "level",
      "type": "integer",
      "uiHint": "slider",
      "group": "basic",
      "order": 1,
      "props": { "min": 1, "max": 100, "step": 1 }
    },
    {
      "name": "role",
      "type": "enum",
      "uiHint": "select",
      "label": "Role",
      "group": "basic",
      "order": 2,
      "options": [
        { "value": "Player", "label": "Player" },
        { "value": "Moderator", "label": "Moderator" },
        { "value": "Admin", "label": "Admin" }
      ]
    },
    {
      "name": "biography",
      "type": "string",
      "uiHint": "textarea",
      "order": 3,
      "props": { "rows": 3 }
    },
    {
      "name": "adminNotes",
      "type": "string",
      "uiHint": "text",
      "label": "Admin Notes",
      "order": 5,
      "conditional": { "field": "role", "operator": "equals", "value": "Admin" }
    }
  ]
}
```

### Table Schema

```json
{
  "name": "Player",
  "columns": [
    { "name": "name",  "type": "string",  "label": "Name",  "sortable": true, "filterable": true },
    { "name": "level", "type": "integer", "label": "Level", "sortable": true, "filterable": false },
    { "name": "role",  "type": "enum",    "label": "Role",  "sortable": true, "filterable": true,
      "options": ["Player", "Moderator", "Admin"] }
  ],
  "pagination": { "defaultPageSize": 20, "pageSizes": [10, 20, 50, 100] },
  "defaultSort": { "column": "name", "direction": "asc" }
}
```

## Frontend Integration

### Razor Pages (server-side)

Use the `FormDescriptor` directly in `.cshtml` — no JSON, no JavaScript needed:

```cshtml
@* Pages/Players/Create.cshtml *@
@{
    var form = Player.GetFormDescriptor();
}

<h3>@form.Name</h3>
<form method="post">
    @foreach (var group in form.Groups.OrderBy(g => g.Order))
    {
        <fieldset>
            <legend>@group.Label</legend>
            @foreach (var field in form.Fields.Where(f => f.Group == group.Name).OrderBy(f => f.Order))
            {
                <div class="form-group">
                    <label asp-for="@field.Name">@field.Label</label>
                    @switch (field.UiHint)
                    {
                        case "text":
                        case "password":
                            <input type="@field.UiHint" name="@field.Name"
                                   placeholder="@field.Placeholder"
                                   class="form-control" />
                            break;
                        case "select":
                            <select name="@field.Name" class="form-control">
                                <option value="">-- Select --</option>
                                @foreach (var opt in field.Options!)
                                {
                                    <option value="@opt.Value">@opt.Label</option>
                                }
                            </select>
                            break;
                        case "textarea":
                            <textarea name="@field.Name" rows="@field.Props!["rows"]"
                                      class="form-control"></textarea>
                            break;
                        case "slider":
                            <input type="range" name="@field.Name"
                                   min="@field.Props!["min"]" max="@field.Props!["max"]" />
                            break;
                        case "checkbox":
                            <input type="checkbox" name="@field.Name" />
                            break;
                        case "datePicker":
                            <input type="date" name="@field.Name" class="form-control" />
                            break;
                    }
                    @if (field.HelpText is not null)
                    {
                        <small class="form-text text-muted">@field.HelpText</small>
                    }
                </div>
            }
        </fieldset>
    }
    <button type="submit" class="btn btn-primary">Save</button>
</form>
```

### Blazor

```razor
@* Fetch schema once, render form dynamically *@
@inject HttpClient Http

@if (_schema is not null)
{
    @foreach (var field in _schema.Fields.OrderBy(f => f.Order))
    {
        <div class="form-group">
            <label>@field.Label</label>
            @switch (field.UiHint)
            {
                case "text":
                    <input type="text" placeholder="@field.Placeholder"
                           @oninput="e => _values[field.Name] = e.Value" />
                    break;
                case "select":
                    <select @onchange="e => _values[field.Name] = e.Value">
                        @foreach (var opt in field.Options ?? [])
                        {
                            <option value="@opt.Value">@opt.Label</option>
                        }
                    </select>
                    break;
                case "slider":
                    <input type="range" min="@field.Props["min"]" max="@field.Props["max"]"
                           @oninput="e => _values[field.Name] = e.Value" />
                    break;
                case "textarea":
                    <textarea rows="@field.Props["rows"]"
                              @oninput="e => _values[field.Name] = e.Value" />
                    break;
                case "checkbox":
                    <input type="checkbox"
                           @onchange="e => _values[field.Name] = e.Value" />
                    break;
            }
        </div>
    }
}

@code {
    private FormSchema? _schema;
    private Dictionary<string, object?> _values = new();

    protected override async Task OnInitializedAsync()
    {
        _schema = await Http.GetFromJsonAsync<FormSchema>("/api/forms/player");
    }
}
```

See [SampleBlazor](sample/SampleBlazor/) for full DynamicField/DynamicForm components with conditional visibility, grouping, mode filtering, and all UI hints.

### React

```tsx
import { useForm, Controller } from 'react-hook-form';

function DynamicForm({ schemaUrl, onSubmit }) {
  const [schema, setSchema] = useState(null);
  const { control, handleSubmit, watch } = useForm();
  const values = watch();

  useEffect(() => {
    fetch(schemaUrl).then(r => r.json()).then(setSchema);
  }, [schemaUrl]);

  if (!schema) return <div>Loading...</div>;

  return (
    <form onSubmit={handleSubmit(onSubmit)}>
      {schema.fields
        .filter(f => !f.hidden)
        .filter(f => !f.conditional || values[f.conditional.field] === f.conditional.value)
        .sort((a, b) => a.order - b.order)
        .map(field => (
          <Controller key={field.name} name={field.name} control={control}
            rules={{ required: field.required && `${field.label} is required` }}
            render={({ field: f, fieldState: { error } }) => (
              <div>
                <label>{field.label}</label>
                {field.uiHint === 'select' ? (
                  <select {...f}>
                    {field.options?.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                  </select>
                ) : field.uiHint === 'slider' ? (
                  <input type="range" {...f} min={field.props?.min} max={field.props?.max} />
                ) : field.uiHint === 'textarea' ? (
                  <textarea {...f} rows={field.props?.rows} />
                ) : (
                  <input type={field.uiHint === 'password' ? 'password' : 'text'} {...f}
                         placeholder={field.placeholder} />
                )}
                {error && <span>{error.message}</span>}
              </div>
            )}
          />
        ))}
      <button type="submit">Save</button>
    </form>
  );
}

// Usage
<DynamicForm schemaUrl="/api/forms/player" onSubmit={data => console.log(data)} />
```

See [react-app](sample/react-app/) for full implementation with DynamicField, DynamicForm, DynamicTable components, validation mapping, and all UI hints.

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
