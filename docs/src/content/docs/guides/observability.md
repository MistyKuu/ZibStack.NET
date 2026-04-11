---
title: Declarative Observability
description: End-to-end guide for [Log] + [Trace] + OpenTelemetry in ZibStack.NET — structured logging, spans, PII masking, and exporter wiring (Jaeger, Seq, OTLP).
---

Instrumenting a .NET service with logs and traces usually means a lot of noisy, duplicative code: `_logger.LogInformation` calls wrapped in `try`/`catch`, `using var activity = ActivitySource.StartActivity(...)`, manual `SetTag` / `SetStatus` / `Dispose` — in every method you care about.

ZibStack ships two attributes that handle all of that at compile time, so business methods stay pure:

- **`[Log]`** (from `ZibStack.NET.Log`) — entry/exit/exception logs with structured properties, zero allocation via compile-time `LoggerMessage.Define` interceptors.
- **`[Trace]`** (from `ZibStack.NET.Aop`) — `System.Diagnostics.Activity` span per call, compatible with any OpenTelemetry exporter.

Both decorate methods or classes, compose with each other, and cost zero runtime reflection.

## Install

```bash
dotnet add package ZibStack.NET.Log     # [Log] + structured interpolated logging
dotnet add package ZibStack.NET.Aop     # [Trace] + AOP runtime
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol   # pick your exporter
dotnet add package OpenTelemetry.Extensions.Hosting
```

## Minimum viable setup

Three code changes plus an attribute or two.

```csharp
// Program.cs
using ZibStack.NET.Aop;

var builder = WebApplication.CreateBuilder(args);

// (1) Register built-in aspect handlers (TraceHandler for [Trace]).
builder.Services.AddAop();

// (2) Wire OpenTelemetry tracing. '*' listens on every ActivitySource
// created by [Trace] (one per decorated class by default).
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("*")
        .AddOtlpExporter());

var app = builder.Build();

// (3) Bridge DI into the aspect runtime. Must run once, before any
// aspect-decorated method is invoked.
app.Services.UseAop();

app.Run();
```

```csharp
// Services/OrderService.cs
using ZibStack.NET.Aop;
using ZibStack.NET.Log;

[Log]      // entry/exit/exception log on every public method
[Trace]    // Activity span on every public method
public class OrderService
{
    private readonly ILogger<OrderService> _logger;
    public OrderService(ILogger<OrderService> logger) => _logger = logger;

    public async Task<Order> PlaceOrderAsync(int customerId, string product, int quantity)
    {
        _logger.LogInformation($"Processing order for customer {customerId}");
        // ... business logic
        return new Order { /* … */ };
    }
}
```

That's the whole setup. Every call to `PlaceOrderAsync` now:

1. Logs an entry line with all parameters (`Entering OrderService.PlaceOrderAsync(customerId: 42, product: "Widget", quantity: 3)`)
2. Opens an `Activity` span named `PlaceOrderAsync` under an `ActivitySource` named `OrderService`
3. Attaches parameters as span tags (`customerId=42`, `product=Widget`, `quantity=3`)
4. Logs the interpolated-string message as **structured** (template `"Processing order for customer {customerId}"` + property `customerId=42`) — not a flat string
5. Logs an exit line with elapsed time and return value on success, or an exception line on failure
6. Closes the span with `Ok` status and `elapsed_ms` tag, or `Error` status + exception tags on failure

## What gets generated

Behind the scenes the ZibStack generators rewrite the class into something like:

```csharp
// PSEUDO — this is the moral equivalent of what the generator emits.
[InterceptsLocation(...)]
public static async Task<Order> PlaceOrderAsync_Intercepted(
    this OrderService @this, int customerId, string product, int quantity)
{
    // [Log] inline emitter
    var logger = AspectServiceProvider.Resolve<ILogger<OrderService>>();
    __entryDelegate(logger, customerId, product, quantity, null);   // cached LoggerMessage.Define<...>
    var sw = Stopwatch.StartNew();

    // [Trace] runtime handler
    var traceHandler = AspectServiceProvider.Resolve<TraceHandler>();
    var ctx = new AspectContext { ClassName = "OrderService", MethodName = "PlaceOrderAsync", /* … */ };
    traceHandler.OnBefore(ctx);

    try
    {
        var result = await @this.PlaceOrderAsync(customerId, product, quantity);
        sw.Stop();
        ctx.ElapsedMilliseconds = sw.ElapsedMilliseconds;
        ctx.ReturnValue = result;
        traceHandler.OnAfter(ctx);
        __exitDelegate(logger, sw.ElapsedMilliseconds, result, null);
        return result;
    }
    catch (Exception ex)
    {
        sw.Stop();
        ctx.ElapsedMilliseconds = sw.ElapsedMilliseconds;
        traceHandler.OnException(ctx, ex);
        __errorDelegate(logger, sw.ElapsedMilliseconds, ex);
        throw;
    }
}
```

Two details that matter:

- **`[Log]` is an "inline emitter"** — the generator writes the logging code directly into the interceptor with zero per-call allocation (one cached `LoggerMessage.Define<T1,T2,T3>` delegate per method).
- **`[Trace]` is a "runtime handler"** — the generator calls `TraceHandler.OnBefore/OnAfter/OnException` through the `IAspectHandler` interface. Handler is a singleton registered by `AddAop()`, so dispatch cost is one virtual call.

You can stack more aspects beyond `[Log]` / `[Trace]` — write your own `IAspectHandler` and they all run in a single generated interceptor. See [AOP → Custom Aspects](/ZibStack.NET/packages/aop/#custom-aspects) for the recipe.

## Structured interpolated logging

The biggest quality-of-life win in `ZibStack.NET.Log` isn't `[Log]` itself — it's that **standard `ILogger` calls with interpolated strings become structured at compile time**:

```csharp
using ZibStack.NET.Log;

_logger.LogInformation($"Processing order for customer {customerId}");
```

That looks like a plain flat string, but because `ZibStack.NET.Log` ships interpolated-string-handler overloads and the source generator intercepts them at call site, what actually ships in the binary is equivalent to:

```csharp
// Cached at static construction:
private static readonly Action<ILogger, int, Exception?> __logHey =
    LoggerMessage.Define<int>(LogLevel.Information,
        new EventId(1, "ProcessingOrder"),
        "Processing order for customer {customerId}");

// At the call site:
__logHey(_logger, customerId, null);
```

Result: structured properties (`customerId=42` as an indexed field in Seq/Elastic/App Insights) + ~40× speedup when the log level is disabled (one `IsEnabled` check, nothing else).

This works with **every** `LogXxx` method — `LogTrace`, `LogDebug`, `LogInformation`, `LogWarning`, `LogError`, `LogCritical` — and all their overloads (with `Exception`, with `EventId`, etc.).

> **"But doesn't `CA2254` tell me not to do this?"** Yes, the built-in Roslyn analyzer `CA2254: Template should be a static expression` warns against `LogInformation($"...")` precisely because Microsoft's own overloads turn the interpolated string into a flat runtime-formatted message. ZibStack.NET.Log's shadowing extension methods invert that: the interpolated-string handler **preserves** the template, and the source generator rewrites the call. It's safe to suppress `CA2254` in projects that reference `ZibStack.NET.Log` — or keep it on if you want an explicit reminder to stay on the structured path.

## PII and sensitive data — `[Sensitive]` / `[NoLog]`

Parameters can be marked so neither `[Log]` nor `[Trace]` leaks them into logs or span tags:

```csharp
using ZibStack.NET.Log;

[Log] [Trace]
public Order PlaceOrder(
    int customerId,
    [Sensitive] string creditCard,   // masked as *** everywhere
    [NoLog]     byte[] rawPayload)   // excluded entirely
{
    // …
}
```

Output:

```
info: OrderService[1] Entering OrderService.PlaceOrder(customerId: 42, creditCard: ***)
```

Span tags:

```
code.namespace = OrderService
code.function  = PlaceOrder
customerId     = 42
creditCard     = ***
# rawPayload is not in the tag list at all
```

**Return-value masking.** If your method returns an object containing PII, decorate the property on the *type*:

```csharp
public class Order
{
    public int Id { get; set; }
    public decimal Total { get; set; }

    [Sensitive]
    public string CustomerEmail { get; set; } = "";
}
```

When `[Log]` serializes the return value for the exit log line, `CustomerEmail` is replaced with `***`. Works with `ObjectLogMode.Destructure` (default) and `ObjectLogMode.Json`.

## Exporter setup by backend

### OTLP / Tempo / any standard collector

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("*")   // every class decorated with [Trace]
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("my-api"))
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri("http://localhost:4317");
            o.Protocol = OtlpExportProtocol.Grpc;
        }));
```

### Jaeger (native OTLP since Jaeger 1.35+)

Same OTLP setup as above, just point at Jaeger's OTLP endpoint (default `http://localhost:4317`). Jaeger UI shows the spans without any extra config.

### Seq (logs + traces in one place)

```bash
dotnet add package Seq.Extensions.Logging
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

```csharp
builder.Services.AddLogging(logging => logging.AddSeq("http://localhost:5341"));

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("*")
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:5341/ingest/otlp")));
```

Structured logs **and** traces from `[Log]` / `[Trace]` flow into the same Seq instance. Bonus: Seq's query language lets you pivot on structured properties directly — `customerId == 42 and @Level == 'Error'` finds every failed order for customer 42 across all services.

### Azure Application Insights

```bash
dotnet add package Azure.Monitor.OpenTelemetry.AspNetCore
```

```csharp
builder.Services.AddOpenTelemetry()
    .UseAzureMonitor(o => o.ConnectionString = "…");
// AddSource("*") is still needed if you want to listen to ZibStack activity sources
```

Every `[Trace]`-decorated method becomes a "dependency" in App Insights with the class name as the operation.

## Tuning `[Trace]`

The default `[Trace]` opens an Activity named after the method, under an ActivitySource named after the class. Override both when needed:

```csharp
// Group spans under a logical service name instead of the class
[Trace(SourceName = "checkout.orders")]
public async Task PlaceOrderAsync(Order order) { … }

// Override the operation name (e.g. for RED metrics aggregation)
[Trace(OperationName = "orders.place")]
public async Task PlaceOrderAsync(Order order) { … }

// Skip parameter tagging on hot paths or wide signatures
[Trace(IncludeParameters = false)]
public IEnumerable<Row> ScanAll(HugeFilter filter) { … }
```

If you used `SourceName = "checkout.orders"`, adjust your exporter listener:

```csharp
tracing.AddSource("checkout.orders")    // explicit instead of "*"
```

## Recipes

### Trace only the slow paths

```csharp
public class OrderService
{
    [Log]
    public Order Validate(Order order) { /* fast, log-only */ }

    [Log] [Trace]
    public async Task<Order> PersistAsync(Order order) { /* slow, worth a span */ }
}
```

Mix and match — `[Log]` and `[Trace]` are independent.

### Custom aspect that mirrors `[Trace]` onto metrics

`[Trace]` produces spans. If you want RED metrics (rate/errors/duration) on the same methods, write a tiny `IAspectHandler`:

```csharp
using System.Diagnostics.Metrics;
using ZibStack.NET.Aop;

[AspectHandler(typeof(MetricsHandler))]
public class MetricsAttribute : AspectAttribute { }

public class MetricsHandler : IAspectHandler
{
    private static readonly Meter Meter = new("MyApp.Methods");
    private static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>("method_duration_ms");

    public void OnBefore(AspectContext ctx) { }

    public void OnAfter(AspectContext ctx)
        => Duration.Record(ctx.ElapsedMilliseconds,
            new("class", ctx.ClassName), new("method", ctx.MethodName), new("status", "ok"));

    public void OnException(AspectContext ctx, Exception ex)
        => Duration.Record(ctx.ElapsedMilliseconds,
            new("class", ctx.ClassName), new("method", ctx.MethodName), new("status", "error"));
}
```

Register once in `Program.cs`:

```csharp
builder.Services.AddAop();
builder.Services.AddSingleton<MetricsHandler>();
```

Apply on any method:

```csharp
[Log] [Trace] [Metrics]
public Task<Order> PlaceOrderAsync(Order o) { … }
```

All three run in a single generated interceptor — no nesting overhead, no reflection.

## Quiet by default, strict on opt-in

`ZibStack.NET.Log` doesn't inject a global using and emits `ZLOG002` at `Info` severity, so installing the package doesn't mutate your existing call sites. When you're ready to migrate legacy `LogInformation("template", arg)` calls to the structured interpolated form, flip strict mode:

```xml
<!-- .csproj -->
<PropertyGroup>
  <ZibLogStrict>true</ZibLogStrict>
</PropertyGroup>
```

That sets `ZibLogEmitGlobalUsing=true` **and** raises `ZLOG002` from `Info` to `Warning` via a bundled `.editorconfig`. See [Log → Configuration](/ZibStack.NET/packages/log/#configuration) for individual toggles and per-file severity overrides.

## Related reference

- [Log — Structured Logging](/ZibStack.NET/packages/log/) — full `[Log]` attribute reference, interpolated-string internals, benchmarks
- [AOP — Aspects & `[Trace]`](/ZibStack.NET/packages/aop/) — built-in `[Trace]` reference + `IAspectHandler` / `IAroundAspectHandler` contract
- [Full CRUD with SQLite](/ZibStack.NET/guides/crud-sqlite/) — the integrated setup used in the §5 observability section of that guide
