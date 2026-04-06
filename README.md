# ZibStack.NET

A collection of .NET source generators and utilities for common application concerns — logging, DTOs, and more.

## Packages

| Package | NuGet | Description |
|---|---|---|
| [**ZibStack.NET.Log**](packages/ZibStack.NET.Log/) | `dotnet add package ZibStack.NET.Log` | Compile-time logging via C# interceptors. Add `[Log]` to any method for automatic entry/exit/exception logging with zero allocation. Also provides interpolated string logging (`LogInformationEx($"...")`). |
| [**ZibStack.NET.Aop**](packages/ZibStack.NET.Aop/) | `dotnet add package ZibStack.NET.Aop` | AOP framework with C# interceptors. AOP framework with C# interceptors. Custom aspects via `IAspectHandler`/`IAroundAspectHandler`. |
| [**ZibStack.NET.Dto**](packages/ZibStack.NET.Dto/) | `dotnet add package ZibStack.NET.Dto` | Source generator for strongly-typed Create and Update request DTOs with PatchField support. |
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
// Your domain model:
public class Player
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public int Score { get; set; }
}

// One line generates Create + Update DTOs:
[DtoFor(typeof(Player), Generate.Create | Generate.Update)]
public partial class PlayerDto { }

// Generated:
// CreatePlayerDto  { Name, Email, Score }              — no Id (auto-generated)
// UpdatePlayerDto  { Name?, Email?, Score? }            — all nullable for partial updates
//   with PatchField<T> support for PATCH endpoints
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
public enum Region { Północ, Południe, Wschód, Zachód }

[Form]
[Table(DefaultSort = "Name", DefaultPageSize = 50)]
[FormGroup("basic", Label = "Dane podstawowe", Order = 1)]
[FormGroup("finance", Label = "Finanse", Order = 2)]

// ERP: hierarchical drill-down
[ChildTable(typeof(CountyView), ForeignKey = "VoivodeshipId", Label = "Powiaty")]
[ChildTable(typeof(PostalCodeView), ForeignKey = "VoivodeshipId", Label = "Kody pocztowe")]

// ERP: per-row action buttons
[RowAction("showDetails", Label = "Szczegóły", Endpoint = "/api/voivodeships/{id}")]
[RowAction("generateReport", Label = "Raport", Icon = "file",
           Endpoint = "/api/voivodeships/{id}/report", Method = "POST",
           Confirmation = "Wygenerować raport?")]

// ERP: global toolbar actions
[ToolbarAction("export", Label = "Eksport do Excel", Icon = "download",
               Endpoint = "/api/voivodeships/export", Method = "GET",
               SelectionMode = "multiple")]
[ToolbarAction("recalculate", Label = "Przelicz salda",
               Endpoint = "/api/voivodeships/recalculate", Method = "POST",
               Confirmation = "Przeliczyć salda?", Permission = "finance.write")]

// ERP: permission metadata
[Permission("voivodeship.read")]
[ColumnPermission("Budget", "finance.read")]
[DataFilter("VoivodeshipId")]
public partial class VoivodeshipView
{
    [FormIgnore]
    [TableColumn(IsVisible = false)]
    public int Id { get; set; }

    [FormField(Label = "Nazwa", Placeholder = "Nazwa województwa", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";

    [FormField(Label = "Kod", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public string Code { get; set; } = "";

    [Select(typeof(Region))]
    [FormField(Label = "Region", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public Region Region { get; set; }

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

    [FormField(Label = "Notatki", Group = "finance")]
    [TextArea(Rows = 3)]
    [TableIgnore]
    public string? Notes { get; set; }
}
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
    { "name": "basic", "label": "Dane podstawowe", "order": 1 },
    { "name": "finance", "label": "Finanse", "order": 2 }
  ],
  "fields": [
    {
      "name": "name", "type": "string", "uiHint": "text",
      "label": "Nazwa", "placeholder": "Nazwa województwa",
      "group": "basic", "order": 0
    },
    {
      "name": "code", "type": "string", "uiHint": "text",
      "label": "Kod", "group": "basic", "order": 1
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
      "name": "notes", "type": "string", "uiHint": "textarea",
      "label": "Notatki", "group": "finance", "order": 3,
      "props": { "rows": 3 }
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
      "sortable": true, "computed": true }
  ],
  "pagination": { "defaultPageSize": 50, "pageSizes": [10, 20, 50, 100] },
  "defaultSort": { "column": "name", "direction": "asc" },
  "children": [
    { "label": "Powiaty", "target": "CountyView", "foreignKey": "voivodeshipId" },
    { "label": "Kody pocztowe", "target": "PostalCodeView", "foreignKey": "voivodeshipId" }
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
