# ZibStack.NET.UI

Source generator for **UI form and table metadata**. Annotate your models and get compile-time form descriptors, table column definitions, and JSON schemas — no reflection, no runtime overhead.

## Features

- **Form metadata** — field types, labels, placeholders, groups, ordering, validation, conditional visibility
- **Table metadata** — columns, sorting, filtering, pagination, formatting
- **UI control hints** — `[Select]`, `[Slider]`, `[TextArea]`, `[DatePicker]`, `[PasswordInput]`, `[FilePicker]`, `[RichText]`, and more
- **ERP features** — hierarchical drill-down, row/toolbar actions, permissions, computed columns, conditional styling
- **Framework-agnostic** — generates neutral C# objects + JSON schema consumable by any UI (Blazor, React, Vue, Angular)
- **Cross-package integration** — auto-detects `ZibStack.NET.Validation` and `ZibStack.NET.Dto` attributes
- **Zero reflection** — everything generated at compile time

## Full Example

```csharp
public enum Region { Północ, Południe, Wschód, Zachód }

// ─── Child table views with their own SchemaUrl ────────────────────
// [ChildTable] resolves SchemaUrl from the target type's [Table] attribute,
// so you declare the URL once on the child — no need to repeat it.

[Table(SchemaUrl = "/api/tables/county")]
public partial class CountyView
{
    public int Id { get; set; }
    [TableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";
    public int VoivodeshipId { get; set; }
}

[Table(SchemaUrl = "/api/tables/postalcode")]
public partial class PostalCodeView
{
    public int Id { get; set; }
    [TableColumn(Sortable = true)]
    public string Code { get; set; } = "";
    [TableColumn(Sortable = true)]
    public string City { get; set; } = "";
    public int VoivodeshipId { get; set; }
}

// ─── Main view — forms + tables + ERP features ────────────────────

[Form]
[Table(DefaultSort = "Name", DefaultPageSize = 50, SchemaUrl = "/api/tables/voivodeship")]
[FormGroup("basic", Label = "Dane podstawowe", Order = 1)]
[FormGroup("contact", Label = "Kontakt", Order = 2)]
[FormGroup("finance", Label = "Finanse", Order = 3)]

// Hierarchical drill-down — SchemaUrl resolved from child's [Table]
[ChildTable(typeof(CountyView), ForeignKey = "VoivodeshipId", Label = "Powiaty")]
[ChildTable(typeof(PostalCodeView), ForeignKey = "VoivodeshipId", Label = "Kody pocztowe")]

// Per-row action buttons
[RowAction("showDetails", Label = "Szczegóły", Endpoint = "/api/voivodeships/{id}")]
[RowAction("generateReport", Label = "Raport", Icon = "file",
           Endpoint = "/api/voivodeships/{id}/report", Method = "POST",
           Confirmation = "Wygenerować raport?")]

// Global toolbar actions
[ToolbarAction("export", Label = "Eksport do Excel", Icon = "download",
               Endpoint = "/api/voivodeships/export", Method = "GET",
               SelectionMode = "multiple")]
[ToolbarAction("recalculate", Label = "Przelicz salda",
               Endpoint = "/api/voivodeships/recalculate", Method = "POST",
               Confirmation = "Przeliczyć salda?", Permission = "finance.write")]

// Permission metadata
[Permission("voivodeship.read")]
[ColumnPermission("Budget", "finance.read")]
[DataFilter("VoivodeshipId")]
public partial class VoivodeshipView
{
    [FormIgnore]
    [TableColumn(IsVisible = false)]
    public int Id { get; set; }

    // Validation: cross-package with ZibStack.NET.Validation / DataAnnotations
    [Required] [MinLength(2)] [MaxLength(100)]
    [FormField(Label = "Nazwa", Placeholder = "Nazwa województwa", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [Required] [Match(@"^[A-Z]{2}$")]
    [FormField(Label = "Kod", HelpText = "Dwuliterowy kod (np. MZ, WP)", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public required string Code { get; set; }

    [Select(typeof(Region))]
    [FormField(Label = "Region", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public Region Region { get; set; }

    [Required] [Email]
    [FormField(Label = "Email kontaktowy", Placeholder = "biuro@wojewodztwo.pl", Group = "contact")]
    [TableIgnore]
    public required string ContactEmail { get; set; }

    [Url]
    [FormField(Label = "Strona WWW", Group = "contact")]
    [TableIgnore]
    public string? Website { get; set; }

    // Computed column with conditional styling
    [FormIgnore]
    [TableColumn(Sortable = true, Label = "Budżet")]
    [Computed]
    [ColumnStyle(When = "value < 0", Severity = "danger")]
    [ColumnStyle(When = "value >= 0", Severity = "success")]
    public decimal Budget { get; set; }

    [FormIgnore]
    [TableColumn(Sortable = true, Label = "Liczba powiatów")]
    [Computed]
    public int CountyCount { get; set; }

    [Range(1900, 2100)]
    [FormField(Label = "Rok utworzenia", Group = "basic")]
    [TableColumn(Sortable = true)]
    public int EstablishedYear { get; set; }

    // Conditional field — only visible when Region == Północ
    [FormConditional("Region", "Północ")]
    [FormField(Label = "Dostęp do morza", Group = "basic")]
    [TableIgnore]
    public bool HasCoastline { get; set; }

    [FormField(Label = "Notatki", Group = "finance")]
    [TextArea(Rows = 3)]
    [TableIgnore]
    public string? Notes { get; set; }

    [FormHidden]
    public int VoivodeshipId { get; set; }
}
```

The generator produces:

```csharp
VoivodeshipView.GetFormDescriptor()    // FormDescriptor object
VoivodeshipView.GetFormSchemaJson()    // Compile-time baked JSON string
VoivodeshipView.GetTableDescriptor()   // TableDescriptor object
VoivodeshipView.GetTableSchemaJson()   // Compile-time baked JSON string
```

Serve via API:

```csharp
app.MapGet("/api/forms/voivodeship", () =>
    Results.Content(VoivodeshipView.GetFormSchemaJson(), "application/json"));

app.MapGet("/api/tables/voivodeship", () =>
    Results.Content(VoivodeshipView.GetTableSchemaJson(), "application/json"));
```

## Generated JSON

<details>
<summary><strong>Form JSON</strong></summary>

```json
{
  "name": "VoivodeshipView",
  "layout": "vertical",
  "groups": [
    { "name": "basic", "label": "Dane podstawowe", "order": 1 },
    { "name": "contact", "label": "Kontakt", "order": 2 },
    { "name": "finance", "label": "Finanse", "order": 3 }
  ],
  "fields": [
    {
      "name": "name", "type": "string", "uiHint": "text",
      "label": "Nazwa", "placeholder": "Nazwa województwa",
      "group": "basic", "order": 0, "required": true,
      "validation": { "required": true, "minLength": 2, "maxLength": 100 }
    },
    {
      "name": "code", "type": "string", "uiHint": "text",
      "label": "Kod", "helpText": "Dwuliterowy kod (np. MZ, WP)",
      "group": "basic", "order": 1, "required": true,
      "validation": { "required": true, "pattern": "^[A-Z]{2}$" }
    },
    {
      "name": "region", "type": "enum", "uiHint": "select",
      "label": "Region", "group": "basic", "order": 2,
      "options": [
        { "value": "Północ", "label": "Północ" },
        { "value": "Południe", "label": "Południe" },
        { "value": "Wschód", "label": "Wschód" },
        { "value": "Zachód", "label": "Zachód" }
      ]
    },
    {
      "name": "contactEmail", "type": "string", "uiHint": "text",
      "label": "Email kontaktowy", "placeholder": "biuro@wojewodztwo.pl",
      "group": "contact", "order": 3, "required": true,
      "validation": { "required": true, "email": true }
    },
    {
      "name": "website", "type": "string", "uiHint": "text",
      "label": "Strona WWW", "group": "contact", "order": 4, "nullable": true,
      "validation": { "url": true }
    },
    {
      "name": "establishedYear", "type": "integer", "uiHint": "number",
      "label": "Rok utworzenia", "group": "basic", "order": 5,
      "validation": { "min": 1900, "max": 2100 }
    },
    {
      "name": "hasCoastline", "type": "boolean", "uiHint": "checkbox",
      "label": "Dostęp do morza", "group": "basic", "order": 6,
      "conditional": { "field": "region", "operator": "equals", "value": "Północ" }
    },
    {
      "name": "notes", "type": "string", "uiHint": "textarea",
      "label": "Notatki", "group": "finance", "order": 7,
      "props": { "rows": 3 }, "nullable": true
    },
    {
      "name": "voivodeshipId", "type": "integer", "uiHint": "number",
      "order": 8, "hidden": true
    }
  ]
}
```
</details>

<details>
<summary><strong>Table JSON</strong></summary>

```json
{
  "name": "VoivodeshipView",
  "schemaUrl": "/api/tables/voivodeship",
  "columns": [
    { "name": "id", "type": "integer", "visible": false },
    { "name": "name", "type": "string", "label": "Nazwa",
      "sortable": true, "filterable": true },
    { "name": "code", "type": "string", "label": "Kod",
      "sortable": true, "filterable": true },
    { "name": "region", "type": "enum", "label": "Region",
      "sortable": true, "filterable": true,
      "options": ["Północ", "Południe", "Wschód", "Zachód"] },
    { "name": "budget", "type": "decimal", "label": "Budżet",
      "sortable": true, "computed": true,
      "styles": [
        { "when": "value < 0", "severity": "danger" },
        { "when": "value >= 0", "severity": "success" }
      ]
    },
    { "name": "countyCount", "type": "integer", "label": "Liczba powiatów",
      "sortable": true, "computed": true },
    { "name": "establishedYear", "type": "integer", "label": "Rok utworzenia",
      "sortable": true }
  ],
  "pagination": { "defaultPageSize": 50, "pageSizes": [10, 20, 50, 100] },
  "defaultSort": { "column": "name", "direction": "asc" },
  "children": [
    { "label": "Powiaty", "target": "CountyView",
      "foreignKey": "voivodeshipId", "schemaUrl": "/api/tables/county" },
    { "label": "Kody pocztowe", "target": "PostalCodeView",
      "foreignKey": "voivodeshipId", "schemaUrl": "/api/tables/postalcode" }
  ],
  "rowActions": [
    { "name": "showDetails", "label": "Szczegóły",
      "endpoint": "/api/voivodeships/{id}", "method": "GET" },
    { "name": "generateReport", "label": "Raport", "icon": "file",
      "endpoint": "/api/voivodeships/{id}/report", "method": "POST",
      "confirmation": "Wygenerować raport?" }
  ],
  "toolbarActions": [
    { "name": "export", "label": "Eksport do Excel", "icon": "download",
      "endpoint": "/api/voivodeships/export", "method": "GET",
      "selectionMode": "multiple" },
    { "name": "recalculate", "label": "Przelicz salda",
      "endpoint": "/api/voivodeships/recalculate", "method": "POST",
      "confirmation": "Przeliczyć salda?", "permission": "finance.write",
      "selectionMode": "none" }
  ],
  "permissions": {
    "view": "voivodeship.read",
    "columns": { "budget": "finance.read" },
    "dataFilters": ["voivodeshipId"]
  }
}
```
</details>

## Database Integration

ZibStack.NET.UI is **DB-agnostic** — it works with any data source. The UI metadata lives on **DTOs/ViewModels**, not on entities, because forms and tables almost never map 1:1 to DB tables.

### Recommended pattern: Entity → DTO → UI metadata

```
EF Core Entity (DB)  →  ZibStack.NET.Dto (generates DTOs)  →  ZibStack.NET.UI (generates UI metadata)  →  JSON  →  Frontend
```

#### 1. Define your entity (database)

```csharp
// Entity — maps to DB table
public class Voivodeship
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public int Population { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }  // soft delete — not on any form

    // Navigation properties
    public ICollection<County> Counties { get; set; } = new List<County>();
}

public class County
{
    public int Id { get; set; }
    public int VoivodeshipId { get; set; }
    public string Name { get; set; } = "";
    public int Population { get; set; }
    public Voivodeship Voivodeship { get; set; } = null!;
}
```

#### 2. Define view models with UI metadata

```csharp
// Create form — only fields the user should fill in, with validation
[Form]
[FormGroup("basic", Label = "Dane podstawowe")]
public partial class CreateVoivodeshipRequest
{
    [Required] [MinLength(2)] [MaxLength(100)]
    [FormField(Label = "Nazwa", Placeholder = "np. Wielkopolskie")]
    public required string Name { get; set; }

    [Required] [Match(@"^[A-Z]{2}$")]
    [FormField(Label = "Kod", Placeholder = "np. WP", HelpText = "Dwuliterowy kod")]
    public required string Code { get; set; }

    [Range(0, 100_000_000)]
    [FormField(Label = "Populacja")]
    public int Population { get; set; }
}

// Child table — declares its own SchemaUrl
[Table(DefaultSort = "Name", SchemaUrl = "/api/tables/county")]
public partial class CountyTableView
{
    [TableColumn(IsVisible = false)]
    public int Id { get; set; }
    public int VoivodeshipId { get; set; }
    [TableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";
    [TableColumn(Sortable = true, Format = "N0")]
    public int Population { get; set; }
}

// Parent table — [ChildTable] resolves SchemaUrl from CountyTableView's [Table]
[Table(DefaultSort = "Name", DefaultPageSize = 50, SchemaUrl = "/api/tables/voivodeship")]
[ChildTable(typeof(CountyTableView), ForeignKey = "VoivodeshipId", Label = "Powiaty")]
[RowAction("edit", Label = "Edytuj", Endpoint = "/api/voivodeships/{id}")]
[ToolbarAction("export", Label = "Eksport", Endpoint = "/api/voivodeships/export",
               SelectionMode = "multiple")]
public partial class VoivodeshipTableView
{
    [TableColumn(IsVisible = false)]
    public int Id { get; set; }

    [TableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";

    [TableColumn(Sortable = true)]
    public string Code { get; set; } = "";

    [TableColumn(Sortable = true, Format = "N0")]
    public int Population { get; set; }

    [TableColumn(Sortable = true)]
    [Computed]
    public int CountyCount { get; set; }

    [TableColumn(Sortable = true, Format = "yyyy-MM-dd")]
    public DateTime CreatedAt { get; set; }
}
```

#### 3. Wire up with EF Core in your API

```csharp
// Serve schemas
app.MapGet("/api/forms/voivodeship", () =>
    Results.Content(CreateVoivodeshipRequest.GetFormSchemaJson(), "application/json"));

app.MapGet("/api/tables/voivodeship", () =>
    Results.Content(VoivodeshipTableView.GetTableSchemaJson(), "application/json"));

// CRUD endpoints
app.MapGet("/api/voivodeships", async (AppDbContext db) =>
{
    var data = await db.Voivodeships
        .Where(v => !v.IsDeleted)
        .Select(v => new VoivodeshipTableView
        {
            Id = v.Id,
            Name = v.Name,
            Code = v.Code,
            Population = v.Population,
            CountyCount = v.Counties.Count,  // computed in SQL
            CreatedAt = v.CreatedAt
        })
        .ToListAsync();
    return Results.Ok(data);
});

app.MapPost("/api/voivodeships", async (CreateVoivodeshipRequest request, AppDbContext db) =>
{
    var entity = new Voivodeship
    {
        Name = request.Name,
        Code = request.Code,
        Population = request.Population,
        CreatedAt = DateTime.UtcNow
    };
    db.Voivodeships.Add(entity);
    await db.SaveChangesAsync();
    return Results.Created($"/api/voivodeships/{entity.Id}", entity.Id);
});

// Child table — filtered by parent ID
app.MapGet("/api/voivodeships/{voivodeshipId}/counties", async (int voivodeshipId, AppDbContext db) =>
{
    var data = await db.Counties
        .Where(c => c.VoivodeshipId == voivodeshipId)
        .Select(c => new CountyTableView
        {
            Id = c.Id,
            VoivodeshipId = c.VoivodeshipId,
            Name = c.Name,
            Population = c.Population
        })
        .ToListAsync();
    return Results.Ok(data);
});
```

The key insight: **entity has internal fields** (IsDeleted, UpdatedAt, navigation properties), **create form has user-facing fields** (Name, Code, Population), **table view has computed columns** (CountyCount). Each serves a different purpose — ZibStack.NET.UI annotates the view models, not the entities.

### Without EF Core

Works the same with Dapper, ADO.NET, REST APIs, or any data source — just annotate your DTOs/ViewModels:

```csharp
[Form]
[Table(DefaultSort = "Name")]
public partial class ProductView
{
    [FormField(Label = "Name")]
    [TableColumn(Sortable = true)]
    public string Name { get; set; } = "";

    [Slider(Min = 0, Max = 10000)]
    [TableColumn(Sortable = true)]
    [ColumnStyle(When = "value < 10", Severity = "warning")]
    public int Stock { get; set; }
}

// With Dapper:
app.MapGet("/api/products", async (IDbConnection db) =>
{
    var data = await db.QueryAsync<ProductView>("SELECT Name, Stock FROM Products");
    return Results.Ok(data);
});
```

## Frontend Integration

### Razor Pages (server-side)

Use the `FormDescriptor` directly in `.cshtml` — no JSON, no JavaScript:

```cshtml
@{
    var form = CreateVoivodeshipRequest.GetFormDescriptor();
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
                    <label>@field.Label</label>
                    @switch (field.UiHint)
                    {
                        case "text":     <input type="text" name="@field.Name" placeholder="@field.Placeholder" /> break;
                        case "number":   <input type="number" name="@field.Name" /> break;
                        case "select":   <select name="@field.Name">@foreach (var o in field.Options!) { <option value="@o.Value">@o.Label</option> }</select> break;
                        case "textarea": <textarea name="@field.Name" rows="@field.Props!["rows"]"></textarea> break;
                        case "checkbox": <input type="checkbox" name="@field.Name" /> break;
                    }
                </div>
            }
        </fieldset>
    }
    <button type="submit">Save</button>
</form>
```

### Blazor

```razor
@inject HttpClient Http

@if (_schema is not null)
{
    @foreach (var field in _schema.Fields.OrderBy(f => f.Order))
    {
        <div class="form-group">
            <label>@field.Label</label>
            @switch (field.UiHint)
            {
                case "text":     <input type="text" placeholder="@field.Placeholder"
                                        @oninput="e => _values[field.Name] = e.Value" /> break;
                case "select":   <select @onchange="e => _values[field.Name] = e.Value">
                                     @foreach (var o in field.Options ?? []) { <option value="@o.Value">@o.Label</option> }
                                 </select> break;
                case "slider":   <input type="range" min="@field.Props["min"]" max="@field.Props["max"]"
                                        @oninput="e => _values[field.Name] = e.Value" /> break;
                case "textarea": <textarea rows="@field.Props["rows"]"
                                           @oninput="e => _values[field.Name] = e.Value" /> break;
            }
        </div>
    }
}

@code {
    private FormSchema? _schema;
    private Dictionary<string, object?> _values = new();

    protected override async Task OnInitializedAsync()
        => _schema = await Http.GetFromJsonAsync<FormSchema>("/api/forms/voivodeship");
}
```

See [SampleBlazor](sample/SampleBlazor/) for full DynamicField/DynamicForm/ErpDemo with drill-down, row actions, and conditional styling.

### React

```tsx
import { useForm, Controller } from 'react-hook-form';

function DynamicForm({ schemaUrl, onSubmit }) {
  const [schema, setSchema] = useState(null);
  const { control, handleSubmit, watch } = useForm();
  const values = watch();

  useEffect(() => { fetch(schemaUrl).then(r => r.json()).then(setSchema); }, [schemaUrl]);
  if (!schema) return <div>Loading...</div>;

  return (
    <form onSubmit={handleSubmit(onSubmit)}>
      {schema.fields
        .filter(f => !f.conditional || values[f.conditional.field] === f.conditional.value)
        .sort((a, b) => a.order - b.order)
        .map(field => (
          <Controller key={field.name} name={field.name} control={control}
            rules={{ required: field.required && `${field.label} is required` }}
            render={({ field: f, fieldState: { error } }) => (
              <div>
                <label>{field.label}</label>
                {field.uiHint === 'select'
                  ? <select {...f}>{field.options?.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}</select>
                  : field.uiHint === 'textarea'
                  ? <textarea {...f} rows={field.props?.rows} />
                  : <input type={field.uiHint === 'password' ? 'password' : 'text'} {...f} placeholder={field.placeholder} />}
                {error && <span>{error.message}</span>}
              </div>
            )} />
        ))}
      <button type="submit">Save</button>
    </form>
  );
}
```

See [react-app](sample/react-app/) for full DynamicField, DynamicForm, DynamicTable, and ErpTable components.

## All Attributes

### Form — Class-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[Form]` | Mark for form generation | `Name?`, `Layout?` |
| `[FormGroup("name")]` | Define field group | `Label?`, `Order?` (AllowMultiple) |

### Form — Property-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[FormField]` | Customize field | `Label?`, `Placeholder?`, `HelpText?`, `Order?`, `Group?` |
| `[FormIgnore]` | Exclude from form | — |
| `[FormHidden]` | In data but not rendered | — |
| `[FormOrder(n)]` | Explicit ordering | `int order` |
| `[FormReadOnly]` | Read-only field | — |
| `[FormDisabled]` | Disabled field | — |
| `[FormSection("group")]` | Assign to group | `string group` |
| `[FormConditional("field", "value")]` | Conditional visibility | `Operator?` |

### UI Control Hints — Property-level

| Attribute | Control | Extra |
|-----------|---------|-------|
| `[TextArea]` | Multi-line text | `Rows?` |
| `[Select]` | Dropdown | `Type enumType` or `params string[]` |
| `[RadioGroup]` | Radio buttons | `Type enumType` or `params string[]` |
| `[Checkbox]` | Toggle | — |
| `[DatePicker]` | Date selector | `Min?`, `Max?` |
| `[TimePicker]` | Time selector | — |
| `[DateTimePicker]` | Date + time | — |
| `[FilePicker]` | File upload | `Accept?`, `Multiple?` |
| `[ColorPicker]` | Color selector | — |
| `[RichText]` | Rich text editor | — |
| `[Slider]` | Range slider | `Min`, `Max`, `Step?` |
| `[PasswordInput]` | Masked input | — |

### Table — Class-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[Table]` | Mark for table generation | `Name?`, `DefaultPageSize?`, `PageSizes?`, `DefaultSort?`, `DefaultSortDirection?`, `SchemaUrl?` |

### Table — Property-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[TableColumn]` | Customize column | `Label?`, `Sortable?`, `Filterable?`, `Format?`, `Order?`, `IsVisible?`, `Width?` |
| `[TableIgnore]` | Exclude from table | — |

### ERP — Class-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[ChildTable(typeof(T))]` | Hierarchical drill-down | `ForeignKey`, `Label`, `SchemaUrl?`* (AllowMultiple) |
| `[RowAction("name")]` | Per-row action button | `Label`, `Icon?`, `Endpoint`, `Method?`, `Confirmation?`, `Permission?` (AllowMultiple) |
| `[ToolbarAction("name")]` | Global toolbar action | `Label`, `Icon?`, `Endpoint`, `Method?`, `Confirmation?`, `Permission?`, `SelectionMode?` (AllowMultiple) |
| `[Permission("name")]` | Required view permission | — |
| `[ColumnPermission("col", "perm")]` | Per-column permission | (AllowMultiple) |
| `[DataFilter("prop")]` | Vertical data filtering | (AllowMultiple) |

### ERP — Property-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[Computed]` | Marks virtual/calculated column | — |
| `[ColumnStyle]` | Conditional styling | `When`, `Severity` (danger/warning/success/info/muted) (AllowMultiple) |

## SchemaUrl Resolution

`[ChildTable]` resolves `SchemaUrl` for drill-down with the following priority:

1. **Explicit** — `[ChildTable(typeof(T), SchemaUrl = "/custom/url")]` on the parent
2. **From target type** — `[Table(SchemaUrl = "/api/tables/county")]` on `T` itself
3. **Convention** — strip `View` suffix, lowercase → `/api/tables/{name}` (e.g. `CountyView` → `/api/tables/county`)

This means you typically declare `SchemaUrl` once on the child type's `[Table]` and it propagates to all parents that reference it.

## Default Behavior

- All public properties included unless `[FormIgnore]` / `[TableIgnore]`
- UI hint auto-detected from C# type: `string` → text, `bool` → checkbox, `enum` → select, `DateTime` → datePicker
- Labels humanized from property names: `FirstName` → "First Name"
- A class can have both `[Form]` and `[Table]`

## Cross-Package Integration

### ZibStack.NET.Validation

When referenced, validation attributes are automatically included in form field metadata:

| Attribute | JSON output |
|-----------|-------------|
| `[Required]` | `"validation": { "required": true }` |
| `[MinLength(n)]` | `"validation": { "minLength": n }` |
| `[MaxLength(n)]` | `"validation": { "maxLength": n }` |
| `[Range(min, max)]` | `"validation": { "min": min, "max": max }` |
| `[Email]` | `"validation": { "email": true }` |
| `[Url]` | `"validation": { "url": true }` |
| `[Match("regex")]` | `"validation": { "pattern": "regex" }` |
| `[NotEmpty]` | `"validation": { "notEmpty": true }` |

Also recognizes `System.ComponentModel.DataAnnotations` equivalents (`[Required]`, `[MinLength]`, `[MaxLength]`, `[Range]`, `[StringLength]`).

### ZibStack.NET.Dto

When referenced, `[CreateOnly]` and `[UpdateOnly]` flags appear in form field descriptors — the client can show/hide fields based on create vs. update mode.

No project-level dependencies — detection is by attribute FQN at compile time.
