# ZibStack.NET

A collection of .NET source generators and utilities for common application concerns — logging, DTOs, and more.

## Packages

| Package | NuGet | Description |
|---|---|---|
| [**ZibStack.NET.Log**](packages/ZibStack.NET.Log/) | `dotnet add package ZibStack.NET.Log` | Compile-time logging via C# interceptors. Add `[Log]` to any method for automatic entry/exit/exception logging with zero allocation. Also provides interpolated string logging (`LogInformationEx($"...")`). |
| [**ZibStack.NET.Aop**](packages/ZibStack.NET.Aop/) | `dotnet add package ZibStack.NET.Aop` | AOP framework with C# interceptors. AOP framework with C# interceptors. Custom aspects via `IAspectHandler`/`IAroundAspectHandler`. |
| [**ZibStack.NET.Dto**](packages/ZibStack.NET.Dto/) | `dotnet add package ZibStack.NET.Dto` | Source generator for CRUD DTOs (Create/Update/Response/Query) with PatchField, validation, and full CRUD API endpoint generation (Minimal API + Controllers). |
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

**Annotate your entity — get DTOs, mapping, query, and full CRUD API:**

```csharp
[CreateDto]                                        // → CreatePlayerRequest with ToEntity()
[UpdateDto]                                        // → UpdatePlayerRequest with ApplyTo()
[ResponseDto]                                      // → PlayerResponse with FromEntity() + ProjectFrom()
[QueryDto(Sortable = true, DefaultSort = "Name")]  // → PlayerQuery with Apply(IQueryable)
[CrudApi(Style = ApiStyle.Both)]                   // → Minimal API endpoints + MVC Controller
public class Player
{
    [DtoIgnore]  public int Id { get; set; }
    public required string Name { get; set; }
    public int Level { get; set; }
    public string? Email { get; set; }

    [CreateOnly]       public required string Password { get; set; }
    [UpdateOnly]       public string? DeactivationReason { get; set; }
    [DtoIgnore]        public bool IsAdmin { get; set; }
    [ResponseIgnore]   public DateTime CreatedAt { get; set; }
}
```

**What gets generated:**

```csharp
// Create request with PatchField<T> for partial payloads + validation:
public record CreatePlayerRequest : ICanCreate<Player>, ICanValidate
{
    public PatchField<string> Name { get; init; }
    public PatchField<int> Level { get; init; }
    public PatchField<string> Email { get; init; }
    public PatchField<string> Password { get; init; }       // CreateOnly
    public List<string> Validate() { ... }
    public Player ToEntity() { ... }
}

// Update request (no Password — CreateOnly; has DeactivationReason — UpdateOnly):
public record UpdatePlayerRequest : ICanApply<Player>, ICanValidate
{
    public PatchField<int> Level { get; init; }
    public PatchField<string> Email { get; init; }
    public PatchField<string> DeactivationReason { get; init; }
    public List<string> Validate() { ... }
    public void ApplyTo(Player target) { ... }
    public List<string> Diff(Player entity) { ... }
}

// Response DTO (no Password — ResponseIgnore):
public record PlayerResponse
{
    public string Name { get; init; }
    public int Level { get; init; }
    public string? Email { get; init; }
    public static PlayerResponse FromEntity(Player entity) { ... }
    public static IQueryable<PlayerResponse> ProjectFrom(IQueryable<Player> query) { ... }
}

// Query DTO — all properties nullable + filter/sort/paginate IQueryable:
public record PlayerQuery
{
    public string? Name { get; init; }
    public int? Level { get; init; }
    public string? SortBy { get; init; }
    public SortDirection? SortDirection { get; init; }
    public IQueryable<Player> Apply(IQueryable<Player> query) { ... }
}
```

**CRUD API — generated endpoints (Minimal API):**

```csharp
// Generated static class — just call app.MapPlayerEndpoints() in Program.cs:
public static class PlayerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder app, string? prefix = null)
    {
        var group = app.MapGroup(prefix ?? "api/players").WithTags("Player");
        group.MapGet("{id}", ...)     // GET    /api/players/{id}     → PlayerResponse
        group.MapGet("", ...)         // GET    /api/players?name=...&sortBy=level&page=2
                                      //        → PaginatedResponse<PlayerResponse>
        group.MapPost("", ...)        // POST   /api/players          → validate + create
        group.MapPatch("{id}", ...)   // PATCH  /api/players/{id}     → validate + apply
        group.MapDelete("{id}", ...)  // DELETE /api/players/{id}
        return app;
    }
}
```

**CRUD API — generated controller (when `Style = ApiStyle.Both` or `Controller`):**

```csharp
[ApiController]
[Route("api/players")]
public partial class PlayerCrudController : ControllerBase
{
    private readonly ICrudStore<Player, int> _store;
    // GetById, GetList, Create, Update, Delete — same logic as Minimal API
}
```

**Setup in Program.cs:**

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new PatchFieldJsonConverterFactory()));

// Register your data access implementation:
builder.Services.AddScoped<ICrudStore<Player, int>, PlayerStore>();

var app = builder.Build();
app.MapPlayerEndpoints();   // generated Minimal API
app.MapControllers();       // picks up generated PlayerCrudController
app.Run();
```

**ICrudStore — implement or use the EF Core base class:**

```csharp
// Option 1: Implement the interface directly
public class PlayerStore : ICrudStore<Player, int>
{
    public ValueTask<Player?> GetByIdAsync(int id, CancellationToken ct) { ... }
    public IQueryable<Player> Query() { ... }
    public ValueTask CreateAsync(Player entity, CancellationToken ct) { ... }
    public ValueTask UpdateAsync(Player entity, CancellationToken ct) { ... }
    public ValueTask DeleteAsync(Player entity, CancellationToken ct) { ... }
}

// Option 2: EF Core — generated base class when EF Core is referenced:
public class PlayerStore : EfCrudStore<Player, int, AppDbContext>
{
    public PlayerStore(AppDbContext db) : base(db) { }
    protected override DbSet<Player> Set => Db.Players;
}
```

<details>
<summary><strong>More DTO features</strong> (click to expand)</summary>

**Combined Create/Update DTO** — single type for both operations:

```csharp
[CreateOrUpdateDto]
public class Team { ... }
// → TeamRequest with ValidateForCreate(), ValidateForUpdate(), ToEntity(), ApplyTo()
```

**External types** — generate DTOs for types you don't control:

```csharp
[CreateDtoFor(typeof(ExternalOrder), Ignore = new[] { "Id" }, Required = new[] { "ProductName" })]
public partial record CreateOrderRequest;

[UpdateDtoFor(typeof(ExternalOrder), Ignore = new[] { "Id" }, Immutable = new[] { "ProductName" })]
public partial record UpdateOrderRequest;
```

**TypeScript-style utilities:**

```csharp
[PartialFrom(typeof(Player))]       // Partial<Player> — all PatchField
public partial record PartialPlayer;

[PickFrom(typeof(Player), "Name", "Email")]   // Pick<Player, "Name" | "Email">
public partial record PlayerContact;

[OmitFrom(typeof(Player), "Password")]        // Omit<Player, "Password">
public partial record SafePlayer;

[IntersectFrom(typeof(Player))]               // Player & Audit
[IntersectFrom(typeof(Audit))]
public partial record PlayerWithAudit;
```

**Flatten nested objects:**

```csharp
public class Player
{
    [Flatten] public Address? Address { get; set; }
    // → AddressStreet, AddressCity, AddressZipCode in generated DTOs
}
```

**CrudApi options:**

```csharp
[CrudApi(
    Route = "api/v2/players",              // custom route
    KeyProperty = "PlayerId",              // non-default key
    Operations = CrudOperations.Read,      // read-only API
    Style = ApiStyle.MinimalApi,           // or Controller, Both
    AuthorizePolicy = "admin"              // require authorization
)]
```

</details>

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
[FormGroup("basic", Label = "Dane podstawowe", Order = 1)]
[FormGroup("contact", Label = "Kontakt", Order = 2)]
[FormGroup("finance", Label = "Finanse", Order = 3)]

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

    // Validation: Required + MinLength → emitted in form JSON for client-side validation
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

    // Relationships — EF Core-style navigation properties
    // FK auto-detected: CountyView.VoivodeshipId matches parent name
    [OneToMany(Label = "Powiaty")]
    public ICollection<CountyView> Counties { get; set; } = new List<CountyView>();

    // Explicit FK with nameof() for compile-time safety
    [OneToMany(ForeignKey = nameof(PostalCodeView.VoivodeshipId), Label = "Kody pocztowe")]
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
<summary><strong>Generated Table JSON</strong> (click to expand)</summary>

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
      "foreignKey": "voivodeshipId", "relation": "oneToMany",
      "schemaUrl": "/api/tables/county" },
    { "label": "Kody pocztowe", "target": "PostalCodeView",
      "foreignKey": "voivodeshipId", "relation": "oneToMany",
      "schemaUrl": "/api/tables/postalcode",
      "formSchemaUrl": "/api/forms/postalcodeview" }
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
