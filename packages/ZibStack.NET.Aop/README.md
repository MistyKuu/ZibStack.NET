# ZibStack.NET.Aop

AOP (Aspect-Oriented Programming) framework for .NET 8+ using **C# interceptors**. Define aspects that run before, after, or on exception of any method — at compile time, no runtime proxy or reflection.

## Install

```
dotnet add package ZibStack.NET.Aop
```

Enable interceptors in your `.csproj`:

```xml
<PropertyGroup>
    <InterceptorsPreviewNamespaces>$(InterceptorsPreviewNamespaces);ZibStack.Generated</InterceptorsPreviewNamespaces>
</PropertyGroup>
```

## Setup (DI)

All aspect handlers are resolved from DI. Wire up once at startup:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register handlers + their dependencies
builder.Services.AddTransient<TimingHandler>();
builder.Services.AddSingleton<ITimingRecorder, MyMetricsRecorder>();

var app = builder.Build();

// Enable DI for all aspects (required):
AspectServiceProvider.ServiceProvider = app.Services;
```

Now any method with `[Timing]`, `[Trace]`, `[Cache]`, etc. will resolve its handler from DI.

## Built-in Aspects

### [Trace] — OpenTelemetry-compatible tracing

Creates `Activity` spans using `System.Diagnostics.ActivitySource`. Works with any OpenTelemetry exporter (Jaeger, Zipkin, OTLP, Application Insights).

```csharp
[Trace]
public async Task<Order> GetOrderAsync(int id) { ... }

// Custom source name:
[Trace(SourceName = "MyApp.Orders")]
public Order PlaceOrder(int customerId) { ... }
```

Parameters are added as span tags. Exceptions set error status + add exception event.

### [Timing] — lightweight metrics

Records method execution time. Register an `ITimingRecorder` in DI to receive metrics.

```csharp
builder.Services.AddSingleton<ITimingRecorder, MyMetricsRecorder>();
builder.Services.AddTransient<TimingHandler>();

[Timing]
public Order GetOrder(int id) { ... }
```

### [Retry] — automatic retry with backoff

```csharp
[Retry(MaxAttempts = 3, DelayMs = 100)]
public Order GetOrder(int id) { ... }

// Exponential backoff: 100ms, 200ms, 400ms
[Retry(MaxAttempts = 4, DelayMs = 100, BackoffMultiplier = 2.0)]
public async Task<Order> GetOrderAsync(int id) { ... }
```

### [Cache] — caching

Register `IAspectCache` in DI (wrap IMemoryCache, Redis, etc.):

```csharp
builder.Services.AddSingleton<IAspectCache, MyMemoryCache>();
builder.Services.AddTransient<CacheHandler>();

[Cache(DurationSeconds = 60)]
public Order GetOrder(int id) { ... }
```

### [RequirePermission] — authorization check

Register `IPermissionChecker` in DI:

```csharp
builder.Services.AddScoped<IPermissionChecker, MyAuthChecker>();
builder.Services.AddTransient<RequirePermissionHandler>();

[RequirePermission]
public void DeleteOrder(int id) { ... }  // checks "DeleteOrder" permission

[RequirePermission(Policy = "Admin")]
public void PurgeAllData() { ... }  // checks "Admin" permission
```

## Dependency Injection

All aspect handlers support DI. Register handlers and their dependencies in your DI container, then wire up `AspectServiceProvider`:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register handler dependencies
builder.Services.AddSingleton<ITimingRecorder, PrometheusTimingRecorder>();
builder.Services.AddScoped<IPermissionChecker, JwtAuthChecker>();
builder.Services.AddSingleton<IAspectCache, RedisAspectCache>();

// Register handlers themselves
builder.Services.AddTransient<TimingHandler>();
builder.Services.AddTransient<RequirePermissionHandler>();
builder.Services.AddTransient<CacheHandler>();

var app = builder.Build();

// Enable DI for all aspect handlers (one line):
AspectServiceProvider.ServiceProvider = app.Services;
```

When `AspectServiceProvider.ServiceProvider` is set, generated code resolves handlers from DI. If DI is not configured or a handler is not registered, it falls back to `new` (backward compatible).

Custom handlers also benefit — just add constructor parameters:

```csharp
public class MetricsHandler : IAspectHandler
{
    private readonly ILogger<MetricsHandler> _logger;
    private readonly IMetrics _metrics;

    public MetricsHandler(ILogger<MetricsHandler> logger, IMetrics metrics)
    {
        _logger = logger;
        _metrics = metrics;
    }

    public void OnBefore(AspectContext ctx) { }
    public void OnAfter(AspectContext ctx)
        => _metrics.RecordHistogram("method_duration", ctx.ElapsedMilliseconds, ctx.MethodName);
    public void OnException(AspectContext ctx, Exception ex)
        => _logger.LogError(ex, "Failed {Method}", ctx.MethodName);
}
```

## Custom Aspects

### Sync handler (works on sync + async methods)

```csharp
[AspectHandler(typeof(MyHandler))]
public class MyAspectAttribute : AspectAttribute { }

public class MyHandler : IAspectHandler
{
    public void OnBefore(AspectContext ctx)
        => Console.WriteLine($"Before {ctx.MethodName}({ctx.FormatParameters()})");
    public void OnAfter(AspectContext ctx)
        => Console.WriteLine($"After {ctx.MethodName} in {ctx.ElapsedMilliseconds}ms");
    public void OnException(AspectContext ctx, Exception ex)
        => Console.WriteLine($"Error in {ctx.MethodName}: {ex.Message}");
}
```

### Around handler — full control over execution

Use `IAroundAspectHandler` to wrap the method call. You control whether to call, how many times, and what to return:

```csharp
[AspectHandler(typeof(CircuitBreakerHandler))]
public class CircuitBreakerAttribute : AspectAttribute { }

public class CircuitBreakerHandler : IAroundAspectHandler
{
    private static int _failures;

    public object? Around(AspectContext ctx, Func<object?> proceed)
    {
        if (_failures > 5)
            throw new Exception("Circuit open — too many failures");

        try
        {
            var result = proceed();
            _failures = 0;
            return result;
        }
        catch
        {
            _failures++;
            throw;
        }
    }
}
```

### Async handler (async methods only)

```csharp
[AspectHandler(typeof(MetricsHandler))]
public class MetricsAttribute : AspectAttribute { }

public class MetricsHandler : IAsyncAspectHandler
{
    public ValueTask OnBeforeAsync(AspectContext ctx) => default;
    public async ValueTask OnAfterAsync(AspectContext ctx)
        => await _client.RecordAsync(ctx.MethodName, ctx.ElapsedMilliseconds);
    public ValueTask OnExceptionAsync(AspectContext ctx, Exception ex) => default;
}
```

## Class-Level Aspects

Apply an aspect to a class — all public instance methods are intercepted:

```csharp
[Log]
[Timing]
public class OrderService
{
    public Order GetOrder(int id) { ... }       // logged + timed
    public void Ping() { ... }                  // logged + timed
    private void Internal() { ... }             // NOT intercepted (private)

    [Log(EntryExitLevel = ZibLogLevel.Debug)]   // method-level overrides class-level
    public void Debug() { ... }
}
```

## Multi-Aspect

Multiple aspects on one method — all run in a single interceptor:

```csharp
[Log]              // zero-overhead inline logging (from ZibStack.NET.Log)
[Trace]            // OpenTelemetry spans
[Timing]           // metrics
[MyCustomAspect]   // your own
public async Task<Order> ProcessOrderAsync(int id) { ... }
```

Execution order controlled by `Order` property (lower = outermost):

```csharp
[Log(Order = 0)]     // OnBefore runs first, OnAfter runs last
[Trace(Order = 1)]   // OnBefore runs second, OnAfter runs second-to-last
```

## AspectContext

Handlers receive rich context with parameter metadata:

```csharp
public void OnBefore(AspectContext ctx)
{
    ctx.ClassName;           // "OrderService"
    ctx.MethodName;          // "GetOrder"
    ctx.Parameters;          // [{ Name="id", Value=42, IsSensitive=false }]
    ctx.FormatParameters();  // "id: 42, creditCard: ***"
    ctx.Properties;          // shared data bag between aspects
}

public void OnAfter(AspectContext ctx)
{
    ctx.ReturnValue;         // the method's return value
    ctx.ElapsedMilliseconds; // execution time
}
```

## Inline Emitters vs Runtime Handlers

ZibStack.NET.Aop supports two aspect execution models. Choose based on your performance needs:

### Runtime Handlers (`IAspectHandler` / `IAroundAspectHandler`)

Simple C# classes. The generator creates handler instances and `AspectContext` at runtime:

```csharp
// What you write:
[Timing]
public Order GetOrder(int id) { ... }

// What the generator produces:
static Order GetOrder_Aop(this OrderService @this, int id)
{
    var handler = AspectServiceProvider.Resolve<TimingHandler>() ?? new TimingHandler();
    var ctx = new AspectContext                    // ← allocation
    {
        ClassName = "OrderService",
        MethodName = "GetOrder",
        Parameters = new[] { new AspectParameterInfo { Name = "id", Value = id } }  // ← allocation + boxing
    };
    handler.OnBefore(ctx);                        // ← virtual dispatch
    var sw = Stopwatch.StartNew();
    var result = @this.GetOrder(id);
    sw.Stop();
    ctx.ElapsedMilliseconds = sw.ElapsedMilliseconds;
    ctx.ReturnValue = result;
    handler.OnAfter(ctx);                         // ← virtual dispatch
    return result;
}
```

**~60ns overhead, ~304B allocation per call.** Easy to write — just implement an interface.

### Inline Emitters (`IAspectEmitter`)

Compile-time code generation. The emitter writes code directly into the interceptor — no objects, no dispatch. Used by `[Log]`:

```csharp
// What [Log] produces (via LogAspectEmitter):
static Order GetOrder_Aop(this OrderService @this, int id)
{
    var logger = __GetLogger(@this);              // UnsafeAccessor, zero overhead
    __logEntry(logger, id, null);                 // pre-compiled LoggerMessage.Define delegate
    var sw = Stopwatch.StartNew();
    var result = @this.GetOrder(id);
    sw.Stop();
    __logExit(logger, sw.ElapsedMilliseconds, result?.ToString(), null);
    return result;
}
```

**~40-50ns overhead, ~64B allocation per call.** Zero boxing, zero virtual dispatch. Requires writing a Roslyn `IAspectEmitter` (advanced).

### When to use which

| | Runtime Handler | Inline Emitter |
|---|---|---|
| **Ease of writing** | Simple C# class | Roslyn code generation |
| **Overhead** | ~60ns, ~304B/call | ~40ns, ~64B/call |
| **Best for** | Most aspects: timing, tracing, auth, cache, retry | Hot paths: logging, metrics |
| **Built-in examples** | `[Timing]`, `[Trace]`, `[Retry]`, `[Cache]`, `[RequirePermission]` | `[Log]` |

Both can be combined on the same method — inline emitters and runtime handlers execute in the same interceptor.

## Requirements

- **.NET 8.0** or later
- `<InterceptorsPreviewNamespaces>ZibStack.Generated</InterceptorsPreviewNamespaces>` in `.csproj`

## License

MIT
