---
title: ZibStack.NET.Aop
description: AOP (Aspect-Oriented Programming) framework for .NET 8+ using C# interceptors — compile-time aspects with no runtime proxy or reflection.
---

[![NuGet](https://img.shields.io/nuget/v/ZibStack.NET.Aop.svg)](https://www.nuget.org/packages/ZibStack.NET.Aop) [![Source](https://img.shields.io/badge/source-GitHub-blue)](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Aop)

AOP (Aspect-Oriented Programming) framework for .NET 8+ using **C# interceptors**. Define aspects that run before, after, or on exception of any method — at compile time, no runtime proxy or reflection.

> **See the working sample:** [SampleApi on GitHub](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Aop/sample/SampleApi)

## Install

```
dotnet add package ZibStack.NET.Aop
```

> The package's `build/.props` enables `InterceptorsNamespaces` for `ZibStack.Generated` automatically on restore — no manual `.csproj` edit required.

## Setup (DI)

All aspect handlers are resolved from DI. There are **two things** you must do at startup:

1. **Register every handler type** in the DI container (`AddTransient` / `AddScoped` / `AddSingleton`).
   Built-in handlers ship with a one-call helper: `AddAop()`.
2. **Bridge the container** to the aspect runtime by calling `UseAop()` after `Build()`.

```csharp
using ZibStack.NET.Aop;

var builder = WebApplication.CreateBuilder(args);

// 1a. Register built-in ZibStack aspect handlers ([Trace], ...).
builder.Services.AddAop();

// 1b. Register any of your own handlers that you reference via [AspectHandler(typeof(...))].
builder.Services.AddTransient<TimingHandler>();
builder.Services.AddSingleton<ITimingRecorder, MyMetricsRecorder>();

var app = builder.Build();

// 2. Bridge DI into the aspect runtime — one call, required once.
app.Services.UseAop();
```

Both steps are mandatory:

- **Forget step 2** → first call into any aspect-decorated method throws:
  > `InvalidOperationException: ZibStack.NET.Aop.AspectServiceProvider.ServiceProvider is not set. [Log] resolves ILogger<T> from DI; you must wire it once at app startup. For ASP.NET Core: 'var app = builder.Build(); app.Services.UseAop();'`
- **Forget step 1** (handler missing from DI) → throws:
  > `InvalidOperationException: Aspect handler 'YourHandler' is not registered in DI. Add 'builder.Services.AddTransient<YourHandler>();' at startup.`

> `UseAop()` is a thin wrapper that sets `AspectServiceProvider.ServiceProvider = services`. If you prefer the assignment form you can still use it — they are equivalent.

You'll see the same error for every handler attribute you stack on a method, so register all of them up-front.

### Dependency injection in handlers

Handlers are resolved from DI — they support **constructor injection** like any other service:

```csharp
public class TimingHandler : IAspectHandler
{
    private readonly ILogger<TimingHandler> _logger;
    private readonly ITimingRecorder _recorder;

    // Dependencies injected automatically by the DI container
    public TimingHandler(ILogger<TimingHandler> logger, ITimingRecorder recorder)
    {
        _logger = logger;
        _recorder = recorder;
    }

    public void OnBefore(AspectContext ctx) { }

    public void OnAfter(AspectContext ctx)
    {
        _logger.LogInformation("{Class}.{Method} completed in {Ms}ms",
            ctx.ClassName, ctx.MethodName, ctx.ElapsedMilliseconds);
        _recorder.Record(ctx.MethodName, ctx.ElapsedMilliseconds);
    }

    public void OnException(AspectContext ctx, Exception ex)
        => _logger.LogWarning(ex, "{Class}.{Method} failed", ctx.ClassName, ctx.MethodName);
}
```

> **Fallback:** If DI is not configured, the generator falls back to `new TimingHandler()` — which requires a parameterless constructor. To use injected dependencies, always set `AspectServiceProvider.ServiceProvider`.

## Built-in: `[Trace]` — OpenTelemetry spans, one attribute

`[Trace]` wraps the decorated method in a `System.Diagnostics.Activity` span, compatible with **OpenTelemetry, Jaeger, Zipkin, Application Insights, and any OTLP exporter**. It ships with `ZibStack.NET.Aop.Abstractions` — no extra package, no handwritten instrumentation.

```csharp
using ZibStack.NET.Aop;

[Trace]
public async Task<Order> GetOrderAsync(int id)
{
    // whatever — the span opens on entry and closes on exit
    return await _repo.FindAsync(id);
}
```

What the handler does automatically:

- Creates (or reuses, thread-safely) an `ActivitySource` per declaring class.
- Starts an `Activity` on entry with kind `Internal`.
- Attaches method parameters as span tags — honoring `[Sensitive]` (masked as `***`) and `[NoLog]` (excluded).
- Adds `code.namespace`, `code.function`, `elapsed_ms` tags following OpenTelemetry semantic conventions.
- Sets `ActivityStatusCode.Ok` on success, `ActivityStatusCode.Error` + exception tags on failure.
- Disposes the activity in both happy and exception paths.

### Options

```csharp
// Group spans under a logical service name instead of the class name:
[Trace(SourceName = "checkout.orders")]
public Task PlaceOrderAsync(Order order) { ... }

// Override the span / operation name:
[Trace(OperationName = "orders.place")]
public Task PlaceOrderAsync(Order order) { ... }

// Skip parameter tags on wide/hot signatures:
[Trace(IncludeParameters = false)]
public IEnumerable<Row> ScanAll(LargeFilter filter) { ... }

// Apply to every public method on a class:
[Trace]
public class OrderService { /* all public methods are traced */ }
```

### Exporter wiring

`[Trace]` only produces `Activity` objects — you still need an OpenTelemetry exporter to see them. A typical setup:

```csharp
builder.Services.AddAop();  // registers TraceHandler

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("*")               // listen to all ZibStack-generated sources (one per class)
        .AddOtlpExporter());          // or AddJaegerExporter / AddZipkinExporter / ...
```

If you used `SourceName = "..."` overrides, add them explicitly instead of `"*"`.

### When to use `[Trace]` vs manual `using var activity = ...`

Use `[Trace]` when you want:
- **Declarative** span boundaries — no `try`/`catch`/`Dispose` boilerplate in every method.
- **Uniform tags** — parameters, timing, class/method names attached consistently.
- **Composability** — stack `[Log]`, `[Trace]`, `[Timing]` without leaking observability code into business logic.

Write manual activities when you need to attach dynamic tags that depend on mid-method state the handler can't see, or when you're creating child spans inside a single method.

## Benchmarks

Runtime handler overhead per call, measured with BenchmarkDotNet on .NET 10.0:

| Method | Mean | Allocated |
|---|---:|---:|
| Direct call (no AOP) | 0.2 ns | 0 B |
| No params (zero-alloc) | 17.4 ns | 104 B |
| **1 runtime handler** | **73.7 ns** | **360 B** |
| **2 stacked handlers** | **106.0 ns** | **672 B** |

~74ns + 360B per handler per call. For typical API endpoints (1-10ms), this is <0.01% overhead.

For hot paths, use an **inline emitter** (`[Log]` does this) — see [Inline Emitters vs Runtime Handlers](#inline-emitters-vs-runtime-handlers).

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
| `IAspectHandler` | Before/After/OnException — sync methods |
| `IAsyncAspectHandler` | Before/After/OnException — async methods |
| `IAroundAspectHandler` | Full control over execution — `object?` return |
| `IAroundAspectHandler<T>` | Full control — **strongly-typed** return `T?` |
| `IAsyncAroundAspectHandler` | Async full control — `object?` return |
| `IAsyncAroundAspectHandler<T>` | Async full control — **strongly-typed** return `T?` |

The generic `<T>` variants are preferred — the generator matches `T` against the intercepted method's return type and generates typed `Func<T?>` instead of `Func<object?>`. Use non-generic for void methods or handlers applied across different return types.

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
| **Built-in** | `[Trace]` (OpenTelemetry spans) | `[Log]` (structured logging) |

Both can be combined on the same method — inline emitters and runtime handlers execute in the same interceptor.

## Requirements

- **.NET 8.0** or later (uses C# interceptors)

The package's `build/.props` enables `InterceptorsNamespaces` automatically on restore — no manual `.csproj` edit needed.

## License

MIT
