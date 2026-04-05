# ZibStack.NET

A collection of .NET source generators and utilities for common application concerns — logging, DTOs, and more.

## Packages

| Package | NuGet | Description |
|---|---|---|
| [**ZibStack.NET.Log**](packages/ZibStack.NET.Log/) | `dotnet add package ZibStack.NET.Log` | Compile-time logging via C# interceptors. Add `[Log]` to any method for automatic entry/exit/exception logging with zero allocation. Also provides interpolated string logging (`LogInformationEx($"...")`). |
| [**ZibStack.NET.Aop**](packages/ZibStack.NET.Aop/) | `dotnet add package ZibStack.NET.Aop` | AOP framework with C# interceptors. Built-in: `[Retry]`, `[Cache]`, `[Authorize]`, `[Trace]`, `[Timing]`. Custom aspects via `IAspectHandler`/`IAroundAspectHandler`. |
| [**ZibStack.NET.Dto**](packages/ZibStack.NET.Dto/) | `dotnet add package ZibStack.NET.Dto` | Source generator for strongly-typed Create and Update request DTOs with PatchField support. |

## Quick Examples

### ZibStack.NET.Log

```csharp
[ZibLog]
public class OrderService
{
    private readonly ILogger<OrderService> _logger;

    [Log]
    public Order PlaceOrder(int customerId, [Sensitive] string creditCard)
    {
        return _repo.Create(customerId, creditCard);
    }
}
// log: Entering OrderService.PlaceOrder(customerId: 42, creditCard: ***)
// log: Exited OrderService.PlaceOrder in 53ms -> {"Id":1,"Product":"Widget"}

// Interpolated string logging:
_logger.LogInformationEx($"User {userId} bought {product} for {total:C}");

// Assembly-level defaults:
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

// Timing — lightweight metrics (DI or static event):
// DI: builder.Services.AddSingleton<ITimingRecorder, MyRecorder>();
// Static: TimingHandler.OnTimingRecorded += (cls, method, ms) => ...;
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
[DtoFor(typeof(Player), Generate.Create | Generate.Update)]
public partial class PlayerDto { }

// Generates: CreatePlayerDto, UpdatePlayerDto with PatchField<T> support
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
│   └── ZibStack.NET.Dto/          → DTO source generator
│       ├── src/                   → Generator
│       ├── tests/                 → Unit tests
│       └── sample/                → Sample API
├── .github/workflows/
│   ├── ci.yml                     → Builds & tests all packages
│   ├── release-aop.yml            → Release ZibStack.NET.Aop to NuGet
│   ├── release-log.yml            → Release ZibStack.NET.Log to NuGet
│   └── release-dto.yml            → Release ZibStack.NET.Dto to NuGet
└── ZibStack.NET.slnx
```

## License

MIT
