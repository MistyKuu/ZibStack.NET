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

Records method execution time. Supports DI with `ITimingRecorder` or static event fallback.

```csharp
// Option 1 — DI (recommended):
builder.Services.AddSingleton<ITimingRecorder, MyMetricsRecorder>();
builder.Services.AddTransient<TimingHandler>();

// Option 2 — static event (no DI needed):
TimingHandler.OnTimingRecorded += (className, methodName, elapsedMs) =>
    Console.WriteLine($"{className}.{methodName}: {elapsedMs}ms");

[Timing]
public Order GetOrder(int id) { ... }
```

### [SuppressException] — exception observability

Fires when a method throws. Supports DI with `IExceptionObserver` or static event fallback.

```csharp
// Option 1 — DI:
builder.Services.AddSingleton<IExceptionObserver, MyExceptionLogger>();
builder.Services.AddTransient<SuppressExceptionHandler>();

// Option 2 — static event:
SuppressExceptionHandler.OnExceptionSuppressed += (ctx, ex) =>
    logger.LogWarning(ex, "Exception in {Method}", ctx.MethodName);

[SuppressException]
public Order? TryGetOrder(int id) { ... }
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

Supports DI with `IAspectCache` (Redis, IMemoryCache, etc.) or built-in in-memory fallback.

```csharp
// Option 1 — DI with custom cache:
builder.Services.AddSingleton<IAspectCache, RedisAspectCache>();
builder.Services.AddTransient<CacheHandler>();

// Option 2 — built-in in-memory (no DI needed):
[Cache(DurationSeconds = 60)]
public Order GetOrder(int id) { ... }

// Invalidate (fallback cache only):
CacheHandler.Invalidate("GetOrder");
CacheHandler.ClearAll();
```

### [Authorize] — authorization check

Supports DI with `IAuthorizationChecker` or static delegate fallback.

```csharp
// Option 1 — DI (recommended):
builder.Services.AddScoped<IAuthorizationChecker, MyAuthChecker>();
builder.Services.AddTransient<AuthorizeHandler>();

// Option 2 — static delegate:
AuthorizeHandler.AuthorizationCheck = (ctx, policy) =>
    currentUser.HasPermission(policy ?? ctx.MethodName);

[Authorize]
public void DeleteOrder(int id) { ... }  // checks "DeleteOrder" permission

[Authorize(Policy = "Admin")]
public void PurgeAllData() { ... }  // checks "Admin" permission
```

## Dependency Injection

All aspect handlers support DI. Register handlers and their dependencies in your DI container, then wire up `AspectServiceProvider`:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register handler dependencies
builder.Services.AddSingleton<ITimingRecorder, PrometheusTimingRecorder>();
builder.Services.AddScoped<IAuthorizationChecker, JwtAuthChecker>();
builder.Services.AddSingleton<IAspectCache, RedisAspectCache>();

// Register handlers themselves
builder.Services.AddTransient<TimingHandler>();
builder.Services.AddTransient<AuthorizeHandler>();
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

## Requirements

- **.NET 8.0** or later
- `<InterceptorsPreviewNamespaces>ZibStack.Generated</InterceptorsPreviewNamespaces>` in `.csproj`

## License

MIT
