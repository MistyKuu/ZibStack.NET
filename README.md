# ZibStack.NET

A collection of .NET source generators and utilities for common application concerns — logging, DTOs, and more.

## Packages

| Package | NuGet | Description |
|---|---|---|
| [**ZibStack.NET.Log**](packages/ZibStack.NET.Log/) | `dotnet add package ZibStack.NET.Log` | Compile-time logging via C# interceptors. Add `[Log]` to any method for automatic entry/exit/exception logging with zero allocation. Also provides interpolated string logging (`LogInformationEx($"...")`). |
| [**ZibStack.NET.Aop**](packages/ZibStack.NET.Aop/) | `dotnet add package ZibStack.NET.Aop` | AOP framework with C# interceptors. AOP framework with C# interceptors. Custom aspects via `IAspectHandler`/`IAroundAspectHandler`. |
| [**ZibStack.NET.Dto**](packages/ZibStack.NET.Dto/) | `dotnet add package ZibStack.NET.Dto` | Source generator for CRUD DTOs (Create/Update/Response/Query) with PatchField support and full CRUD API generation. |
| [**ZibStack.NET.Result**](packages/ZibStack.NET.Result/) | `dotnet add package ZibStack.NET.Result` | Functional Result monad (`Result<T>`) with Map/Bind/Match, error handling without exceptions. |
| [**ZibStack.NET.Validation**](packages/ZibStack.NET.Validation/) | `dotnet add package ZibStack.NET.Validation` | Source generator for compile-time validation from attributes (`[Required]`, `[Email]`, `[Range]`, `[Match]`). |
| [**ZibStack.NET.UI**](packages/ZibStack.NET.UI/) | `dotnet add package ZibStack.NET.UI` | Source generator for UI form and table metadata — forms, tables, drill-down, row/toolbar actions, permissions, conditional styling. |

## Quick Examples

### ZibStack.NET.Log

```csharp
// On a method:
[Log]
public Order PlaceOrder(int customerId, [Sensitive] string creditCard) { ... }
// log: Entering OrderService.PlaceOrder(customerId: 42, creditCard: ***)
// log: Exited OrderService.PlaceOrder in 53ms -> {"Id":1,"Product":"Widget"}

// On a class — logs ALL public methods:
[Log]
public class OrderService { ... }

// Interpolated string logging:
logger.LogInformationEx($"User {userId} bought {product} for {total:C}");

// Optional: override assembly-level defaults (default: Information level, Destructure mode)
[assembly: ZibLogDefaults(EntryExitLevel = ZibLogLevel.Debug, ObjectLogging = ObjectLogMode.Json)]
```

### ZibStack.NET.Aop

```csharp
// Define a custom aspect — just a class + attribute:
[AspectHandler(typeof(TimingHandler))]
public class TimingAttribute : AspectAttribute { }

public class TimingHandler : IAspectHandler
{
    public void OnBefore(AspectContext ctx)
        => Console.WriteLine($"Starting {ctx.MethodName}({ctx.FormatParameters()})");
    public void OnAfter(AspectContext ctx)
        => Console.WriteLine($"Completed {ctx.MethodName} in {ctx.ElapsedMilliseconds}ms");
    public void OnException(AspectContext ctx, Exception ex)
        => Console.WriteLine($"Failed {ctx.MethodName}: {ex.Message}");
}

// Apply it:
[Timing]
public Order GetOrder(int id) { ... }
```

Built-in aspects (no extra dependencies):

```csharp
// OpenTelemetry-compatible tracing — creates Activity spans:
[Trace]
public async Task<Order> GetOrderAsync(int id) { ... }
// → Jaeger/Zipkin/OTLP see: OrderService.GetOrderAsync with params as tags

// Timing — lightweight metrics via DI:
// builder.Services.AddSingleton<ITimingRecorder, MyRecorder>();
[Timing]
public Order PlaceOrder(int id) { ... }

// Multi-aspect — combine freely:
[Log]
[Trace]
[Timing]
public async Task<Order> ProcessOrderAsync(int id) { ... }
```

Async handlers (for async methods only):

```csharp
public class MetricsHandler : IAsyncAspectHandler
{
    public ValueTask OnBeforeAsync(AspectContext ctx) => default;
    public async ValueTask OnAfterAsync(AspectContext ctx)
        => await _client.RecordAsync(ctx.MethodName, ctx.ElapsedMilliseconds);
    public ValueTask OnExceptionAsync(AspectContext ctx, Exception ex) => default;
}
```

### ZibStack.NET.Dto

```csharp
[CreateDto]                                        // → CreatePlayerRequest with ToEntity()
[UpdateDto]                                        // → UpdatePlayerRequest with ApplyTo()
[ResponseDto]                                      // → PlayerResponse with FromEntity() + ProjectFrom()
[QueryDto(Sortable = true, DefaultSort = "Name")]  // → PlayerQuery with Apply(IQueryable)
[CrudApi(Style = ApiStyle.Both)]                   // → Minimal API + MVC Controller
public class Player
{
    [DtoIgnore]  public int Id { get; set; }
    public required string Name { get; set; }
    public int Level { get; set; }
    public string? Email { get; set; }

    [CreateOnly]     public required string Password { get; set; }
    [UpdateOnly]     public string? DeactivationReason { get; set; }
    [ResponseIgnore] public DateTime CreatedAt { get; set; }
}

// Program.cs — that's it:
builder.Services.AddScoped<ICrudStore<Player, int>, PlayerStore>();
app.MapPlayerEndpoints();   // GET/POST/PATCH/DELETE api/players
app.MapControllers();       // generated PlayerCrudController
```

### ZibStack.NET.Result

```csharp
public Result<Order> GetOrder(int id)
{
    if (id <= 0) return Result<Order>.Failure(Error.Validation("Invalid ID"));
    var order = _repo.Find(id);
    return order is null ? Result<Order>.Failure(Error.NotFound("Order not found")) 
                         : Result<Order>.Success(order);
}

// Usage with Map/Bind/Match:
var result = GetOrder(42)
    .Map(o => o.Total)
    .Match(
        onSuccess: total => $"Total: {total}",
        onFailure: error => $"Error: {error.Message}");
```

### ZibStack.NET.Validation

```csharp
[Validate]
public partial class CreateUserRequest
{
    [Required] [MinLength(2)] public string Name { get; set; } = "";
    [Required] [Email]        public string Email { get; set; } = "";
    [Range(18, 120)]          public int Age { get; set; }
    [Match(@"^\+?\d{7,15}$")] public string? Phone { get; set; }
}

// Generated Validate() method:
var result = request.Validate();
if (!result.IsValid) return BadRequest(result.Errors);
```

### ZibStack.NET.UI

```csharp
public enum Region { North, South, East, West }

// ─── Child views with [Table] for SchemaUrl ────────────────────────

[Table(SchemaUrl = "/api/tables/county")]
public partial class CountyView
{
    public int Id { get; set; }
    [TableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";
    public int VoivodeshipId { get; set; }
}

[Table(SchemaUrl = "/api/tables/postalcode")]
[Form]
public partial class PostalCodeView
{
    public int Id { get; set; }
    [TableColumn(Sortable = true)]
    public string Code { get; set; } = "";
    [TableColumn(Sortable = true)]
    public string City { get; set; } = "";
    public int VoivodeshipId { get; set; }
}

// ─── Main view — relationships + ERP features ──────────────────────

[Form]
[Table(DefaultSort = "Name", DefaultPageSize = 50, SchemaUrl = "/api/tables/voivodeship")]
[FormGroup("basic", Label = "Basic Info", Order = 1)]
[FormGroup("contact", Label = "Contact", Order = 2)]
[FormGroup("finance", Label = "Finance", Order = 3)]

// ERP: per-row action buttons
[RowAction("showDetails", Label = "Details", Endpoint = "/api/voivodeships/{id}")]
[RowAction("generateReport", Label = "Report", Icon = "file",
           Endpoint = "/api/voivodeships/{id}/report", Method = "POST",
           Confirmation = "Generate report?")]

// ERP: global toolbar actions
[ToolbarAction("export", Label = "Export to Excel", Icon = "download",
               Endpoint = "/api/voivodeships/export", Method = "GET",
               SelectionMode = "multiple")]
[ToolbarAction("recalculate", Label = "Recalculate",
               Endpoint = "/api/voivodeships/recalculate", Method = "POST",
               Confirmation = "Recalculate balances?", Permission = "finance.write")]

// ERP: permission metadata
[Permission("voivodeship.read")]
[ColumnPermission("Budget", "finance.read")]
[DataFilter("VoivodeshipId")]
public partial class VoivodeshipView
{
    [FormIgnore]
    [TableColumn(IsVisible = false)]
    public int Id { get; set; }

    // Validation: Required + MinLength → emitted in form JSON for client-side validation
    [Required] [MinLength(2)] [MaxLength(100)]
    [FormField(Label = "Name", Placeholder = "Enter name...", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [Required] [Match(@"^[A-Z]{2}$")]
    [FormField(Label = "Code", HelpText = "Two-letter code (e.g. NY, CA)", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public required string Code { get; set; }

    [Select(typeof(Region))]
    [FormField(Label = "Region", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public Region Region { get; set; }

    [Required] [Email]
    [FormField(Label = "Contact Email", Placeholder = "office@example.com", Group = "contact")]
    [TableIgnore]
    public required string ContactEmail { get; set; }

    [Url]
    [FormField(Label = "Website", Group = "contact")]
    [TableIgnore]
    public string? Website { get; set; }

    // Computed column with conditional styling
    [FormIgnore]
    [TableColumn(Sortable = true, Label = "Budget")]
    [Computed]
    [ColumnStyle(When = "value < 0", Severity = "danger")]
    [ColumnStyle(When = "value >= 0", Severity = "success")]
    public decimal Budget { get; set; }

    [FormIgnore]
    [TableColumn(Sortable = true, Label = "County Count")]
    [Computed]
    public int CountyCount { get; set; }

    [Range(1900, 2100)]
    [FormField(Label = "Established Year", Group = "basic")]
    [TableColumn(Sortable = true)]
    public int EstablishedYear { get; set; }

    // Conditional field — only visible when Region == North
    [FormConditional("Region", "North")]
    [FormField(Label = "Has Coastline", Group = "basic")]
    [TableIgnore]
    public bool HasCoastline { get; set; }

    [FormField(Label = "Notes", Group = "finance")]
    [TextArea(Rows = 3)]
    [TableIgnore]
    public string? Notes { get; set; }

    [FormHidden]
    public int VoivodeshipId { get; set; }

    // Relationships — EF Core-style navigation properties
    // FK auto-detected: CountyView.VoivodeshipId matches parent name
    [OneToMany(Label = "Counties")]
    public ICollection<CountyView> Counties { get; set; } = new List<CountyView>();

    // Explicit FK with nameof() for compile-time safety
    [OneToMany(ForeignKey = nameof(PostalCodeView.VoivodeshipId), Label = "Postal Codes")]
    public ICollection<PostalCodeView> PostalCodes { get; set; } = new List<PostalCodeView>();
}
```

Add `[Entity]` to generate EF Core configuration from the same model:

```csharp
// Generated: IEntityTypeConfiguration<VoivodeshipView> with HasMany, HasKey, Ignore, etc.
// Register in DbContext:
protected override void OnModelCreating(ModelBuilder builder)
    => builder.ApplyGeneratedConfigurations();
```

Serve the generated JSON to any frontend (React, Vue, Angular, Blazor):

```csharp
app.MapGet("/api/forms/voivodeship", () =>
    Results.Content(VoivodeshipView.GetFormSchemaJson(), "application/json"));
app.MapGet("/api/tables/voivodeship", () =>
    Results.Content(VoivodeshipView.GetTableSchemaJson(), "application/json"));
```

<details>
<summary><strong>Generated Form JSON</strong> (click to expand)</summary>

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
<summary><strong>Generated Table JSON</strong> (click to expand)</summary>

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
      "schemaUrl": "/api/tables/postalcode",
      "formSchemaUrl": "/api/forms/postalcodeview" }
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

## Repository Structure

```
ZibStack.NET/
├── packages/
│   ├── ZibStack.NET.Aop/          → AOP framework (aspects, interceptors)
│   │   ├── src/                   → Generator + Abstractions
│   │   └── sample/                → Sample with custom aspects
│   ├── ZibStack.NET.Log/          → Logging source generator
│   │   ├── src/                   → Generator + Abstractions
│   │   ├── tests/                 → Unit tests + Benchmarks
│   │   └── sample/                → Sample API
│   ├── ZibStack.NET.Dto/          → DTO source generator
│   │   ├── src/                   → Generator
│   │   ├── tests/                 → Unit tests
│   │   └── sample/                → Sample API
│   ├── ZibStack.NET.Result/       → Result monad (Map/Bind/Match)
│   │   ├── src/                   → Library
│   │   └── tests/                 → Unit tests
│   ├── ZibStack.NET.Validation/   → Validation source generator
│   │   ├── src/                   → Generator
│   │   └── tests/                 → Unit tests
│   └── ZibStack.NET.UI/           → UI metadata source generator
│       ├── src/                   → Generator
│       ├── tests/                 → Unit tests
│       └── sample/                → API + Blazor + React samples
├── .github/workflows/
│   ├── ci.yml                     → Builds & tests all packages
│   ├── release-aop.yml            → Release ZibStack.NET.Aop to NuGet
│   ├── release-log.yml            → Release ZibStack.NET.Log to NuGet
│   ├── release-dto.yml            → Release ZibStack.NET.Dto to NuGet
│   ├── release-result.yml         → Release ZibStack.NET.Result to NuGet
│   ├── release-validation.yml     → Release ZibStack.NET.Validation to NuGet
│   └── release-ui.yml             → Release ZibStack.NET.UI to NuGet
└── ZibStack.NET.slnx
```

## Support

If you find ZibStack.NET useful, consider buying me a coffee:

<a href="https://buymeacoffee.com/mistykuu" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" height="40"></a>

## License

MIT
