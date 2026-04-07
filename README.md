# ZibStack.NET

A collection of .NET source generators and utilities for common application concerns — logging, DTOs, and more.

## Packages

| Package | NuGet | Description |
|---|---|---|
| [**ZibStack.NET.Log**](packages/ZibStack.NET.Log/) | `dotnet add package ZibStack.NET.Log` | Compile-time logging via C# interceptors. Add `[Log]` to any method for automatic entry/exit/exception logging with zero allocation. Also provides interpolated string logging (`LogInformationEx($"...")`). |
| [**ZibStack.NET.Aop**](packages/ZibStack.NET.Aop/) | `dotnet add package ZibStack.NET.Aop` | AOP framework with C# interceptors. Custom aspects via `IAspectHandler`/`IAroundAspectHandler`. |
| [**ZibStack.NET.Utils**](packages/ZibStack.NET.Utils/) | `dotnet add package ZibStack.NET.Utils` | Source generator for utility types: `PatchField<T>`, `PaginatedResponse<T>`, `PartialFrom`, `IntersectFrom`, `PickFrom`, `OmitFrom`. |
| [**ZibStack.NET.Dto**](packages/ZibStack.NET.Dto/) | `dotnet add package ZibStack.NET.Dto` | Source generator for CRUD DTOs (Create/Update/Response/Query) with PatchField support and full CRUD API generation. Requires `ZibStack.NET.Utils`. |
| [**ZibStack.NET.Result**](packages/ZibStack.NET.Result/) | `dotnet add package ZibStack.NET.Result` | Functional Result monad (`Result<T>`) with Map/Bind/Match, error handling without exceptions. |
| [**ZibStack.NET.Validation**](packages/ZibStack.NET.Validation/) | `dotnet add package ZibStack.NET.Validation` | Source generator for compile-time validation from attributes (`[Required]`, `[Email]`, `[Range]`, `[Match]`). |

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
│   ├── ZibStack.NET.Utils/         → Utility types (PatchField, PaginatedResponse, etc.)
│   │   └── src/                   → Generator
│   ├── ZibStack.NET.Dto/          → DTO source generator (uses Utils)
│   │   ├── src/                   → Generator
│   │   ├── tests/                 → Unit tests
│   │   └── sample/                → Sample API
│   ├── ZibStack.NET.Result/       → Result monad (Map/Bind/Match)
│   │   ├── src/                   → Library
│   │   └── tests/                 → Unit tests
│   ├── ZibStack.NET.Validation/   → Validation source generator
│   │   ├── src/                   → Generator
│   │   └── tests/                 → Unit tests
├── .github/workflows/
│   ├── ci.yml                     → Builds & tests all packages
│   ├── release-aop.yml            → Release ZibStack.NET.Aop to NuGet
│   ├── release-log.yml            → Release ZibStack.NET.Log to NuGet
│   ├── release-utils.yml           → Release ZibStack.NET.Utils to NuGet
│   ├── release-dto.yml            → Release ZibStack.NET.Dto to NuGet
│   ├── release-result.yml         → Release ZibStack.NET.Result to NuGet
│   └── release-validation.yml     → Release ZibStack.NET.Validation to NuGet
└── ZibStack.NET.slnx
```

## Support

If you find ZibStack.NET useful, consider buying me a coffee:

<a href="https://buymeacoffee.com/mistykuu" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" height="40"></a>

## License

MIT
