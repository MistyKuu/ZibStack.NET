---
title: Custom aspects & internals
description: Write your own [Aspect] handlers (sync / around / async), class-level + multi-aspect application, AspectContext API, and inline emitters vs runtime handlers.
---

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

Use `IAroundAspectHandler` to wrap the method call. You control whether to call, how many times, and what to return.

**Strongly-typed (recommended)** — use `IAroundAspectHandler<T>` for typed `proceed` and return value:

```csharp
[AspectHandler(typeof(CacheHandler))]
public class CacheAttribute : AspectAttribute { }

public class CacheHandler : IAroundAspectHandler<Order>
{
    private readonly Dictionary<string, Order> _cache = new();

    public Order? Around(AspectContext ctx, Func<Order?> proceed)
    {
        var key = $"{ctx.MethodName}:{ctx.FormatParameters()}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var result = proceed();    // strongly typed!
        if (result is not null) _cache[key] = result;
        return result;
    }
}

// Usage:
[Cache]
public Order GetOrder(int id) { ... }  // T matches Order
```

**Non-generic (for void or mixed return types)** — use `IAroundAspectHandler` with `object?`:

```csharp
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

### Runtime Handlers

Simple C# classes. The generator creates handler instances and `AspectContext` at runtime.

**Handler interfaces:**

| Interface | Use case |
|-----------|----------|
| `IAspectHandler` | Before/After/OnException — sync methods (also works on async as fallback) |
| `IAsyncAspectHandler` | Before/After/OnException — async methods only |
| `IAroundAspectHandler` | Full control over execution — `object?` return, sync methods |
| `IAroundAspectHandler<T>` | Full control — **strongly-typed** return `T?`, sync methods |
| `IAsyncAroundAspectHandler` | Async full control — `object?` return |
| `IAsyncAroundAspectHandler<T>` | Async full control — **strongly-typed** return `T?` |

The generic `<T>` variants are preferred — the generator matches `T` against the intercepted method's return type and generates typed `Func<T?>` instead of `Func<object?>`. Use non-generic for void methods or handlers applied across different return types.

**Dual interface support:** A handler can implement both sync and async interfaces (e.g. `IAroundAspectHandler` + `IAsyncAroundAspectHandler`). The generator picks the right one based on the target method — async path for async methods, sync path for sync methods. The built-in `RetryHandler` uses this pattern.

**Example:**

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

Compile-time code generation. The emitter writes code directly into the AOP interceptor — no objects, no dispatch. Used by the `[Log]` **attribute** (not to be confused with interpolated-string `logger.LogInformation($"...")` calls, which use a different mechanism — see [Log → How `LogInformation($"...")` actually works](/ZibStack.NET/packages/log/#in-depth-how-loginformation-actually-works)):

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
| **Best for** | Most aspects: timing, tracing, auth, cache, retry | Hot paths: method-level entry/exit logging, metrics |
| **Built-in** | `[Trace]` (OpenTelemetry spans) | `[Log]` attribute (method-level entry/exit logs) |

Both can be combined on the same method — inline emitters and runtime handlers execute in the same interceptor.

