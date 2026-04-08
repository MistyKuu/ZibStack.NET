# ZibStack.NET

A collection of .NET source generators and utilities for common application concerns — logging, DTOs, and more.

**[Documentation](https://mistykuu.github.io/ZibStack.NET/)** | **[Getting Started](https://mistykuu.github.io/ZibStack.NET/getting-started/)**

## Packages

| Package | NuGet | Description |
|---|---|---|
| [**ZibStack.NET.Log**](packages/ZibStack.NET.Log/) | `dotnet add package ZibStack.NET.Log` | Compile-time logging via C# interceptors. Add `[Log]` to any method for automatic entry/exit/exception logging with zero allocation. Also provides interpolated string logging (`LogInformationEx($"...")`). |
| [**ZibStack.NET.Aop**](packages/ZibStack.NET.Aop/) | `dotnet add package ZibStack.NET.Aop` | AOP framework with C# interceptors. Custom aspects via `IAspectHandler`/`IAroundAspectHandler`. |
| [**ZibStack.NET.Core**](packages/ZibStack.NET.Core/) | `dotnet add package ZibStack.NET.Core` | Source generator for shared attributes: relationships (`OneToMany`, `OneToOne`, `Entity`), TypeScript-style utility types (`PartialFrom`, `IntersectFrom`, `PickFrom`, `OmitFrom`). |
| [**ZibStack.NET.Dto**](packages/ZibStack.NET.Dto/) | `dotnet add package ZibStack.NET.Dto` | Source generator for CRUD DTOs (Create/Update/Response/Query) with PatchField support and full CRUD API generation. |
| [**ZibStack.NET.Query**](packages/ZibStack.NET.Query/) | `dotnet add package ZibStack.NET.Query` | Filter/sort DSL for REST APIs. Parses query strings (`filter=Level>25,Team.Name=*ski&sort=-Level`) into LINQ/SQL. Compile-time field allowlists via source generation. |
| [**ZibStack.NET.Result**](packages/ZibStack.NET.Result/) | `dotnet add package ZibStack.NET.Result` | Functional Result monad (`Result<T>`) with Map/Bind/Match, error handling without exceptions. |
| [**ZibStack.NET.EntityFramework**](packages/ZibStack.NET.EntityFramework/) | `dotnet add package ZibStack.NET.EntityFramework` | EF Core integration for Dto CRUD API. Auto-generates stores + DI registration from `DbContext`. |
| [**ZibStack.NET.Dapper**](packages/ZibStack.NET.Dapper/) | `dotnet add package ZibStack.NET.Dapper` | Dapper integration for Dto CRUD API. `DapperCrudStore` base class with auto-generated SQL. |
| [**ZibStack.NET.Validation**](packages/ZibStack.NET.Validation/) | `dotnet add package ZibStack.NET.Validation` | Source generator for compile-time validation from attributes (`[Required]`, `[Email]`, `[Range]`, `[Match]`). |
| [**ZibStack.NET.UI**](packages/ZibStack.NET.UI/) | `dotnet add package ZibStack.NET.UI` | Source generator for UI form/table metadata. Annotate models, get compile-time form descriptors and table column definitions. |

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
// One attribute = full CRUD API with auto-generated DTOs + endpoints:
[CrudApi]
public class Player
{
    [DtoIgnore]  public int Id { get; set; }
    public required string Name { get; set; }
    public int Level { get; set; }
    public string? Email { get; set; }

    [CreateOnly]     public required string Password { get; set; }
    [ResponseIgnore] public DateTime CreatedAt { get; set; }
}

// EF Core — auto-generated stores from DbContext:
[GenerateCrudStores]
public class AppDbContext : DbContext
{
    public DbSet<Player> Players => Set<Player>();
}

// Program.cs — three lines:
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite("Data Source=app.db"));
builder.Services.AddAppDbContextCrudStores();   // auto-generated DI registration
app.MapPlayerEndpoints();                        // auto-generated GET/POST/PATCH/DELETE
```

### ZibStack.NET.Query

```csharp
// Add ZibStack.NET.Query to your project — the Dto generator auto-detects it
// and adds filter/sort string params to all CRUD list endpoints:

GET /api/players?filter=Level>25,Name=*ski&sort=-Level&page=1&pageSize=20
GET /api/players?filter=Team.Name=Lakers                    // relation → auto JOIN
GET /api/players?filter=(Level>50|Level<10),Team.City=LA    // OR + grouping
GET /api/players?filter=Name=in=Jan;Anna;Kasia              // IN list
GET /api/players?filter=Email=*@test.pl/i&sort=Team.Name    // case insensitive + relation sort

// Operators: = != > >= < <= =* !* ^ !^ $ !$ =in= =out=
// Logic: , (AND) | (OR) () (grouping) /i (case insensitive)
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
// Annotate models for forms + tables — generates JSON metadata at compile time:
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

    [Select(typeof(Region))]
    [FormField(Label = "Region", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public Region Region { get; set; }

    [Slider(Min = 1, Max = 100)]
    [FormField(Label = "Level", Group = "basic")]
    [TableColumn(Sortable = true)]
    public int Level { get; set; }
}

// Generated: PlayerViewFormDescriptor, PlayerViewTableDescriptor, PlayerViewJsonSchema
// → Consume from Blazor, React, Vue, Angular — framework-agnostic JSON metadata
```

## Repository Structure

```
ZibStack.NET/
├── packages/
│   ├── ZibStack.NET.Aop/              → AOP framework (aspects, interceptors)
│   ├── ZibStack.NET.Log/              → Logging source generator
│   ├── ZibStack.NET.Core/             → Shared attributes (relations, utility types)
│   ├── ZibStack.NET.Dto/              → DTO + CRUD API source generator
│   ├── ZibStack.NET.Query/            → Filter/sort DSL for REST APIs
│   ├── ZibStack.NET.EntityFramework/  → EF Core integration for Dto CRUD
│   ├── ZibStack.NET.Dapper/           → Dapper integration for Dto CRUD
│   ├── ZibStack.NET.Result/           → Result monad (Map/Bind/Match)
│   ├── ZibStack.NET.Validation/       → Validation source generator
│   └── ZibStack.NET.UI/               → UI form/table metadata generator
├── .github/workflows/
│   ├── ci.yml                         → Builds & tests all packages
│   ├── release-all.yml                → Release all packages to NuGet
│   └── release-{package}.yml          → Individual package releases
└── ZibStack.NET.slnx
```

## Support

If you find ZibStack.NET useful, consider buying me a coffee:

<a href="https://buymeacoffee.com/mistykuu" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" height="40"></a>

## License

MIT
