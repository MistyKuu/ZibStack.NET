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


## Read more

- [Generated JSON schemas](/ZibStack.NET/packages/ui/json-schemas/) — what each emitted descriptor looks like and how it's exposed.
- [Relationships (`[OneToMany]` / `[OneToOne]`)](/ZibStack.NET/packages/ui/relationships/) — modeling navigation for relation pickers and drill-downs.
- [Database integration](/ZibStack.NET/packages/ui/database/) — `[Entity]` + EF Core wiring driven from the same metadata.
- [Frontend integration](/ZibStack.NET/packages/ui/frontend/) — SPA consumption: schemaUrl, runtime endpoints, dynamic rendering.
- [All attributes](/ZibStack.NET/packages/ui/attributes/) — full parameter reference.
