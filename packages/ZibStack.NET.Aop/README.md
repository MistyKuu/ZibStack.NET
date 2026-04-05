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

Records method execution time via a static event. No external dependencies.

```csharp
// Subscribe to timing events:
TimingHandler.OnTimingRecorded += (className, methodName, elapsedMs) =>
{
    Console.WriteLine($"{className}.{methodName}: {elapsedMs}ms");
    // or: myMetrics.RecordHistogram("method_duration", elapsedMs, methodName);
};

[Timing]
public Order GetOrder(int id) { ... }
```

### [SuppressException] — exception observability

Fires an event when a method throws. Useful for recording exceptions without changing control flow.

```csharp
SuppressExceptionHandler.OnExceptionSuppressed += (ctx, ex) =>
    logger.LogWarning(ex, "Exception in {Method}", ctx.MethodName);

[SuppressException]
public Order? TryGetOrder(int id) { ... }
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
