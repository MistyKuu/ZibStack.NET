---
title: ZibStack.NET.UI
description: Source generator for UI form and table metadata — compile-time form descriptors, table column definitions, and JSON schemas with no reflection.
---

[![NuGet](https://img.shields.io/nuget/v/ZibStack.NET.UI.svg)](https://www.nuget.org/packages/ZibStack.NET.UI) [![Source](https://img.shields.io/badge/source-GitHub-blue)](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.UI)

Source generator for **UI form and table metadata**. Annotate your models and get compile-time form descriptors, table column definitions, and JSON schemas — no reflection, no runtime overhead.

![ImTiredOfCrud Demo](../../../assets/imtiredofcrud-demo.png)

*One `[ImTiredOfCrud]` attribute generates: CRUD API + DTOs + form/table UI schemas + Query DSL. The frontend reads JSON schemas and renders a data grid with filtering/sorting and a form with validation — zero configuration.*

> **See the working samples:** [SampleApi](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.UI/sample/SampleApi) | [SampleBlazor](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.UI/sample/SampleBlazor) | [React App](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.UI/sample/react-app)

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
public enum Region { North, South, East, West }

// ─── Child table views with their own SchemaUrl ────────────────────
// [OneToMany] resolves SchemaUrl from the target type's [UiTable] attribute,
// so you declare the URL once on the child — no need to repeat it.

[UiTable(SchemaUrl = "/api/tables/county")]
public partial class CountyView
{
    public int Id { get; set; }
    [UiTableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";
    public int VoivodeshipId { get; set; }
}

[UiTable(SchemaUrl = "/api/tables/postalcode")]
public partial class PostalCodeView
{
    public int Id { get; set; }
    [UiTableColumn(Sortable = true)]
    public string Code { get; set; } = "";
    [UiTableColumn(Sortable = true)]
    public string City { get; set; } = "";
    public int VoivodeshipId { get; set; }
}

// ─── Main view — forms + tables + ERP features ────────────────────

[UiForm]
[UiTable(DefaultSort = "Name", DefaultPageSize = 50, SchemaUrl = "/api/tables/voivodeship")]
[UiFormGroup("basic", Label = "Basic Info", Order = 1)]
[UiFormGroup("contact", Label = "Contact", Order = 2)]
[UiFormGroup("finance", Label = "Finance", Order = 3)]

// Per-row action buttons
[RowAction("showDetails", Label = "Details", Endpoint = "/api/voivodeships/{id}")]
[RowAction("generateReport", Label = "Report", Icon = "file",
           Endpoint = "/api/voivodeships/{id}/report", Method = "POST",
           Confirmation = "Generate report?")]

// Global toolbar actions
[ToolbarAction("export", Label = "Export to Excel", Icon = "download",
               Endpoint = "/api/voivodeships/export", Method = "GET",
               SelectionMode = "multiple")]
[ToolbarAction("recalculate", Label = "Recalculate",
               Endpoint = "/api/voivodeships/recalculate", Method = "POST",
               Confirmation = "Recalculate balances?", Permission = "finance.write")]

// Permission metadata
[Permission("voivodeship.read")]
[ColumnPermission("Budget", "finance.read")]
[DataFilter("VoivodeshipId")]
public partial class VoivodeshipView
{
    [UiFormIgnore]
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    // Validation: cross-package with ZibStack.NET.Validation / DataAnnotations
    [ZRequired] [ZMinLength(2)] [ZMaxLength(100)]
    [UiFormField(Label = "Name", Placeholder = "Enter name...", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [ZRequired] [ZMatch(@"^[A-Z]{2}$")]
    [UiFormField(Label = "Code", HelpText = "Two-letter code (e.g. NY, CA)", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public required string Code { get; set; }

    [Select(typeof(Region))]
    [UiFormField(Label = "Region", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public Region Region { get; set; }

    [ZRequired] [ZEmail]
    [UiFormField(Label = "Contact Email", Placeholder = "office@example.com", Group = "contact")]
    [UiTableIgnore]
    public required string ContactEmail { get; set; }

    [ZUrl]
    [UiFormField(Label = "Website", Group = "contact")]
    [UiTableIgnore]
    public string? Website { get; set; }

    // Computed column with conditional styling
    [UiFormIgnore]
    [UiTableColumn(Sortable = true, Label = "Budget")]
    [Computed]
    [ColumnStyle(When = "value < 0", Severity = "danger")]
    [ColumnStyle(When = "value >= 0", Severity = "success")]
    public decimal Budget { get; set; }

    [UiFormIgnore]
    [UiTableColumn(Sortable = true, Label = "County Count")]
    [Computed]
    public int CountyCount { get; set; }

    [ZRange(1900, 2100)]
    [UiFormField(Label = "Established Year", Group = "basic")]
    [UiTableColumn(Sortable = true)]
    public int EstablishedYear { get; set; }

    // Conditional field — only visible when Region == North
    [UiFormConditional("Region", "North")]
    [UiFormField(Label = "Has Coastline", Group = "basic")]
    [UiTableIgnore]
    public bool HasCoastline { get; set; }

    [UiFormField(Label = "Notes", Group = "finance")]
    [TextArea(Rows = 3)]
    [UiTableIgnore]
    public string? Notes { get; set; }

    [UiFormHidden]
    public int VoivodeshipId { get; set; }

    [OneToMany(Label = "Counties")]
    public ICollection<CountyView> Counties { get; set; } = new List<CountyView>();

    [OneToMany(ForeignKey = nameof(PostalCodeView.VoivodeshipId), Label = "Postal Codes")]
    public ICollection<PostalCodeView> PostalCodes { get; set; } = new List<PostalCodeView>();
}
```

The generator produces:

```csharp
VoivodeshipView.GetFormDescriptor()    // FormDescriptor object
VoivodeshipView.GetFormSchemaJson()    // Compile-time baked JSON string
VoivodeshipView.GetTableDescriptor()   // TableDescriptor object
VoivodeshipView.GetTableSchemaJson()   // Compile-time baked JSON string
```

Serve via API. One-liner — the generator emits a `MapZibStackUiSchemas()` extension method that registers `GET /api/forms/{name}` and `GET /api/tables/{name}` for every type in the assembly that has `[UiForm]` / `[UiTable]` / `[ImTiredOfCrud]`:

```csharp
using ZibStack.NET.UI;
// ...
var app = builder.Build();
app.MapZibStackUiSchemas();
// Registers all your form and table schema endpoints at once.
// The {name} segment is the type name lower-cased
// (e.g. VoivodeshipView → /api/forms/voivodeshipview).
```

If you'd rather wire each endpoint by hand (e.g. to customise the route or
add authorisation), you can still call the generated static methods directly:

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
    { "name": "basic", "label": "Basic Info", "order": 1 },
    { "name": "contact", "label": "Contact", "order": 2 },
    { "name": "finance", "label": "Finance", "order": 3 }
  ],
  "fields": [
    {
      "name": "name", "type": "string", "uiHint": "text",
      "label": "Name", "placeholder": "Enter name...",
      "group": "basic", "order": 0, "required": true,
      "validation": { "required": true, "minLength": 2, "maxLength": 100 }
    },
    {
      "name": "code", "type": "string", "uiHint": "text",
      "label": "Code", "helpText": "Two-letter code (e.g. NY, CA)",
      "group": "basic", "order": 1, "required": true,
      "validation": { "required": true, "pattern": "^[A-Z]{2}$" }
    },
    {
      "name": "region", "type": "enum", "uiHint": "select",
      "label": "Region", "group": "basic", "order": 2,
      "options": [
        { "value": "North", "label": "North" },
        { "value": "South", "label": "South" },
        { "value": "East", "label": "East" },
        { "value": "West", "label": "West" }
      ]
    },
    {
      "name": "contactEmail", "type": "string", "uiHint": "text",
      "label": "Contact Email", "placeholder": "office@example.com",
      "group": "contact", "order": 3, "required": true,
      "validation": { "required": true, "email": true }
    },
    {
      "name": "website", "type": "string", "uiHint": "text",
      "label": "Website", "group": "contact", "order": 4, "nullable": true,
      "validation": { "url": true }
    },
    {
      "name": "establishedYear", "type": "integer", "uiHint": "number",
      "label": "Established Year", "group": "basic", "order": 5,
      "validation": { "min": 1900, "max": 2100 }
    },
    {
      "name": "hasCoastline", "type": "boolean", "uiHint": "checkbox",
      "label": "Has Coastline", "group": "basic", "order": 6,
      "conditional": { "field": "region", "operator": "equals", "value": "North" }
    },
    {
      "name": "notes", "type": "string", "uiHint": "textarea",
      "label": "Notes", "group": "finance", "order": 7,
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
    { "name": "name", "type": "string", "label": "Name",
      "sortable": true, "filterable": true },
    { "name": "code", "type": "string", "label": "Code",
      "sortable": true, "filterable": true },
    { "name": "region", "type": "enum", "label": "Region",
      "sortable": true, "filterable": true,
      "options": ["North", "South", "East", "West"] },
    { "name": "budget", "type": "decimal", "label": "Budget",
      "sortable": true, "computed": true,
      "styles": [
        { "when": "value < 0", "severity": "danger" },
        { "when": "value >= 0", "severity": "success" }
      ]
    },
    { "name": "countyCount", "type": "integer", "label": "County Count",
      "sortable": true, "computed": true },
    { "name": "establishedYear", "type": "integer", "label": "Established Year",
      "sortable": true }
  ],
  "pagination": { "defaultPageSize": 50, "pageSizes": [10, 20, 50, 100] },
  "defaultSort": { "column": "name", "direction": "asc" },
  "children": [
    { "label": "Counties", "target": "CountyView",
      "foreignKey": "voivodeshipId", "relation": "oneToMany",
      "schemaUrl": "/api/tables/county" },
    { "label": "Postal Codes", "target": "PostalCodeView",
      "foreignKey": "voivodeshipId", "relation": "oneToMany",
      "schemaUrl": "/api/tables/postalcode" }
  ],
  "rowActions": [
    { "name": "showDetails", "label": "Details",
      "endpoint": "/api/voivodeships/{id}", "method": "GET" },
    { "name": "generateReport", "label": "Report", "icon": "file",
      "endpoint": "/api/voivodeships/{id}/report", "method": "POST",
      "confirmation": "Generate report?" }
  ],
  "toolbarActions": [
    { "name": "export", "label": "Export to Excel", "icon": "download",
      "endpoint": "/api/voivodeships/export", "method": "GET",
      "selectionMode": "multiple" },
    { "name": "recalculate", "label": "Recalculate",
      "endpoint": "/api/voivodeships/recalculate", "method": "POST",
      "confirmation": "Recalculate balances?", "permission": "finance.write",
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

### API Metadata in JSON Schemas

When `[ImTiredOfCrud]` or `[CrudApi]` is present, the generated JSON schemas include API metadata:

**Table schema:**
```json
{
  "apiUrl": "/api/products",
  "keyProperty": "id",
  "columns": [
    {
      "name": "name",
      "type": "string",
      "filterable": true,
      "filterOperators": ["=", "!=", "=*", "!*", "^", "!^", "$", "!$", "=in=", "=out="]
    }
  ]
}
```

**Form schema:**
```json
{
  "apiUrl": "/api/products",
  "keyProperty": "id"
}
```

`apiUrl` — CRUD endpoint URL. `keyProperty` — ID field for GET/PATCH/DELETE by id.
`filterOperators` — per-column operators based on type (string: 10 ops, numeric: 8, enum: 4, boolean: 2).

## Relationships (`[OneToMany]` / `[OneToOne]`)

Define relationships on navigation properties — a single declaration drives both table drill-down and form sub-forms:

```csharp
[UiTable(SchemaUrl = "/api/tables/task")]
[UiForm]
public partial class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int ProjectId { get; set; }  // FK auto-detected by convention
}

[UiForm]
public partial class ProjectSettings
{
    public int Id { get; set; }
    public string Theme { get; set; } = "";
}

[UiForm]
[UiTable(DefaultSort = "Name", SchemaUrl = "/api/tables/project")]
public partial class ProjectView
{
    [UiFormIgnore]
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [UiFormField(Label = "Project Name")]
    [UiTableColumn(Sortable = true)]
    public string Name { get; set; } = "";

    public int SettingsId { get; set; }

    // One-to-many: FK auto-detected as TaskItem.ProjectId
    [OneToMany(Label = "Tasks")]
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();

    // Explicit FK via nameof() for compile-time safety
    [OneToMany(ForeignKey = nameof(Attachment.ProjectId), Label = "Attachments")]
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

    // One-to-one: FK auto-detected as ProjectView.SettingsId
    [OneToOne(Label = "Settings")]
    public ProjectSettings? Settings { get; set; }
}
```

Navigation properties are automatically excluded from form fields and table columns.

### Foreign Key Resolution

1. **Explicit** — `[OneToMany(ForeignKey = nameof(Child.ParentId))]` (compile-time safe)
2. **Convention (OneToMany)** — looks for `{ParentTypeName}Id` on the child type (strips `View` suffix)
3. **Convention (OneToOne)** — looks for `{NavigationPropertyName}Id` on the parent type

### SchemaUrl / FormSchemaUrl Resolution

Both `[OneToMany]` and `[OneToOne]` resolve URLs with the same priority:
1. Explicit property on the attribute
2. From target type's `[UiTable]` / `[UiForm]` attribute
3. Convention fallback (e.g. `/api/tables/{name}`, `/api/forms/{name}`)

### Generated JSON

Table JSON includes a `relation` field:
```json
{
  "children": [
    {
      "label": "Tasks", "target": "TaskItem",
      "foreignKey": "projectId", "relation": "oneToMany",
      "schemaUrl": "/api/tables/task", "formSchemaUrl": "/api/forms/taskitem"
    },
    {
      "label": "Settings", "target": "ProjectSettings",
      "foreignKey": "settingsId", "relation": "oneToOne",
      "formSchemaUrl": "/api/forms/projectsettings"
    }
  ]
}
```

Form JSON also includes a `children` block:
```json
{
  "children": [
    {
      "name": "tasks", "label": "Tasks", "target": "TaskItem",
      "foreignKey": "projectId", "relation": "oneToMany",
      "schemaUrl": "/api/tables/task", "formSchemaUrl": "/api/forms/taskitem"
    }
  ]
}
```

### Backward Compatibility


## EF Core Integration (`[Entity]`)

Add `[Entity]` to any model class to generate `IEntityTypeConfiguration<T>` at compile time. The same class serves as both UI model and EF Core entity — no separate entity class needed.

Requires `Microsoft.EntityFrameworkCore` and a relational provider (e.g. `Microsoft.EntityFrameworkCore.SqlServer`) in your project. Generation is skipped automatically if EF Core is not referenced.

```csharp
[UiForm]
[UiTable(DefaultSort = "Name", SchemaUrl = "/api/tables/project")]
[Entity(TableName = "Projects")]
public partial class ProjectView
{
    public int Id { get; set; }

    [UiFormField(Label = "Project Name")]
    [UiTableColumn(Sortable = true)]
    public string Name { get; set; } = "";

    public int SettingsId { get; set; }

    [Computed]
    public int TaskCount { get; set; }

    [OneToMany(Label = "Tasks")]
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();

    [OneToOne(Label = "Settings")]
    public ProjectSettings? Settings { get; set; }
}
```

Generated at compile time:

```csharp
partial class ProjectView : IEntityTypeConfiguration<ProjectView>
{
    void IEntityTypeConfiguration<ProjectView>.Configure(EntityTypeBuilder<ProjectView> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(e => e.Id);
        builder.Ignore(e => e.TaskCount);
        builder.HasMany(e => e.Tasks).WithOne().HasForeignKey("ProjectId");
        builder.HasOne(e => e.Settings).WithOne().HasForeignKey<ProjectView>(e => e.SettingsId);
    }
}
```

Register all generated configurations in your `DbContext`:

```csharp
public class AppDbContext : DbContext
{
    public DbSet<ProjectView> Projects => Set<ProjectView>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder builder)
        => builder.ApplyGeneratedConfigurations();
}
```

### What gets generated

| Source | EF Core output |
|--------|---------------|
| `[Entity(TableName = "X")]` | `builder.ToTable("X")` |
| `[Entity(Schema = "dbo")]` | `builder.ToTable("...", "dbo")` |
| Property named `Id` or `{Class}Id` | `builder.HasKey(e => e.Id)` |
| `[Computed]` | `builder.Ignore(e => e.Prop)` |
| `[OneToMany]` on `ICollection<T>` | `builder.HasMany(e => e.Nav).WithOne().HasForeignKey(...)` |
| `[OneToOne]` on navigation prop | `builder.HasOne(e => e.Nav).WithOne().HasForeignKey<T>(...)` |

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
[UiForm]
[UiFormGroup("basic", Label = "Basic Info")]
public partial class CreateVoivodeshipRequest
{
    [ZRequired] [ZMinLength(2)] [ZMaxLength(100)]
    [UiFormField(Label = "Name", Placeholder = "e.g. California")]
    public required string Name { get; set; }

    [ZRequired] [ZMatch(@"^[A-Z]{2}$")]
    [UiFormField(Label = "Code", Placeholder = "e.g. CA", HelpText = "Two-letter code")]
    public required string Code { get; set; }

    [ZRange(0, 100_000_000)]
    [UiFormField(Label = "Population")]
    public int Population { get; set; }
}

// Child table — declares its own SchemaUrl
[UiTable(DefaultSort = "Name", SchemaUrl = "/api/tables/county")]
public partial class CountyTableView
{
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }
    public int VoivodeshipId { get; set; }
    [UiTableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";
    [UiTableColumn(Sortable = true, Format = "N0")]
    public int Population { get; set; }
}

[UiTable(DefaultSort = "Name", DefaultPageSize = 50, SchemaUrl = "/api/tables/voivodeship")]
[RowAction("edit", Label = "Edit", Endpoint = "/api/voivodeships/{id}")]
[ToolbarAction("export", Label = "Export", Endpoint = "/api/voivodeships/export",
               SelectionMode = "multiple")]
public partial class VoivodeshipTableView
{
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [UiTableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";

    [UiTableColumn(Sortable = true)]
    public string Code { get; set; } = "";

    [UiTableColumn(Sortable = true, Format = "N0")]
    public int Population { get; set; }

    [UiTableColumn(Sortable = true)]
    [Computed]
    public int CountyCount { get; set; }

    [UiTableColumn(Sortable = true, Format = "yyyy-MM-dd")]
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
[UiForm]
[UiTable(DefaultSort = "Name")]
public partial class ProductView
{
    [UiFormField(Label = "Name")]
    [UiTableColumn(Sortable = true)]
    public string Name { get; set; } = "";

    [Slider(Min = 0, Max = 10000)]
    [UiTableColumn(Sortable = true)]
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
| `[UiForm]` | Mark for form generation | `Name?`, `Layout?` |
| `[UiFormGroup("name")]` | Define field group | `Label?`, `Order?` (AllowMultiple) |

### Form — Property-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[UiFormField]` | Customize field | `Label?`, `Placeholder?`, `HelpText?`, `Order?`, `Group?` |
| `[UiFormIgnore]` | Exclude from form | — |
| `[UiFormHidden]` | In data but not rendered | — |
| `[UiFormOrder(n)]` | Explicit ordering | `int order` |
| `[UiFormReadOnly]` | Read-only field | — |
| `[UiFormDisabled]` | Disabled field | — |
| `[UiFormSection("group")]` | Assign to group | `string group` |
| `[UiFormConditional("field", "value")]` | Conditional visibility | `Operator?` |

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
| `[UiTable]` | Mark for table generation | `Name?`, `DefaultPageSize?`, `PageSizes?`, `DefaultSort?`, `DefaultSortDirection?`, `SchemaUrl?` |

### Table — Property-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[UiTableColumn]` | Customize column | `Label?`, `Sortable?`, `Filterable?`, `Format?`, `Order?`, `IsVisible?`, `Width?` |
| `[UiTableIgnore]` | Exclude from table | — |

### Relationships — Property-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[OneToMany]` | One-to-many on `ICollection<T>` | `ForeignKey?`, `Label?`, `SchemaUrl?`, `FormSchemaUrl?` |
| `[OneToOne]` | One-to-one on navigation property | `ForeignKey?`, `Label?`, `SchemaUrl?`, `FormSchemaUrl?` |

### Entity — Class-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
| `[Entity]` | Generate `IEntityTypeConfiguration<T>` for EF Core | `TableName?`, `Schema?` |

### ERP — Class-level

| Attribute | Purpose | Parameters |
|-----------|---------|-----------|
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


1. **Explicit** — `SchemaUrl = "/custom/url"` on the attribute itself
2. **From target type** — `[UiTable(SchemaUrl = "/api/tables/county")]` on `T` itself
3. **Convention** — strip `View` suffix, lowercase → `/api/tables/{name}` (e.g. `CountyView` → `/api/tables/county`)

`FormSchemaUrl` (available on `[OneToMany]`/`[OneToOne]`) follows the same pattern but checks for `[UiForm]` on the target type.

This means you typically declare `SchemaUrl` once on the child type's `[UiTable]` and it propagates to all parents that reference it.

## Default Behavior

- All public properties included unless `[UiFormIgnore]` / `[UiTableIgnore]`
- UI hint auto-detected from C# type: `string` → text, `bool` → checkbox, `enum` → select, `DateTime` → datePicker
- Labels humanized from property names: `FirstName` → "First Name"
- A class can have both `[UiForm]` and `[UiTable]`

## Cross-Package Integration

### ZibStack.NET.Validation

When referenced, validation attributes are automatically included in form field metadata:

| Attribute | JSON output |
|-----------|-------------|
| `[ZRequired]` | `"validation": { "required": true }` |
| `[ZMinLength(n)]` | `"validation": { "minLength": n }` |
| `[ZMaxLength(n)]` | `"validation": { "maxLength": n }` |
| `[ZRange(min, max)]` | `"validation": { "min": min, "max": max }` |
| `[ZEmail]` | `"validation": { "email": true }` |
| `[ZUrl]` | `"validation": { "url": true }` |
| `[ZMatch("regex")]` | `"validation": { "pattern": "regex" }` |
| `[ZNotEmpty]` | `"validation": { "notEmpty": true }` |

Also recognizes `System.ComponentModel.DataAnnotations` equivalents (`[ZRequired]`, `[ZMinLength]`, `[ZMaxLength]`, `[ZRange]`, `[StringLength]`).

### ZibStack.NET.Dto

When referenced, `[CreateOnly]` and `[UpdateOnly]` flags appear in form field descriptors — the client can show/hide fields based on create vs. update mode.

No project-level dependencies — detection is by attribute FQN at compile time.
