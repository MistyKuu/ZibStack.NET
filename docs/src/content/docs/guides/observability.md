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

// (1) Register built-in aspect handlers ([Trace], [Retry], [Cache], [Metrics], ...).
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

- **`[Log]` is an "inline emitter"** — the generator writes the logging code directly into the AOP interceptor with zero per-call allocation (one cached `LoggerMessage.Define<T1,T2,T3>` delegate per decorated method).
- **`[Trace]` is a "runtime handler"** — the generator calls `TraceHandler.OnBefore/OnAfter/OnException` through the `IAspectHandler` interface. Handler is a singleton registered by `AddAop()`, so dispatch cost is one virtual call.

You can stack more aspects beyond `[Log]` / `[Trace]` — write your own `IAspectHandler` and they all run in a single generated interceptor. See [AOP → Custom Aspects](/ZibStack.NET/packages/aop/#custom-aspects) for the recipe.

> **Not to be confused with interpolated-string logging.** The `[Log]` attribute path above rewrites the **method definition** to wrap each call with entry/exit/exception code. The interpolated-string structured-logging path (shown in the next section) rewrites individual **`logger.LogXxx($"...")` call sites** inside the method body. Both use source-generated interceptors and both end up dispatching through `LoggerMessage.Define`, but they target different things and can be used independently. A method can have `[Log]` without containing any interpolated-string logs, and vice versa.

## Structured interpolated logging

The biggest quality-of-life win in `ZibStack.NET.Log` isn't `[Log]` itself — it's that **standard `ILogger` calls with interpolated strings become structured at compile time**:

```csharp
using ZibStack.NET.Log;

_logger.LogInformation($"Processing order for customer {customerId}");
```

That looks like a plain flat string, but it isn't. The mechanism is two layers stacked:

1. **Extension-method shadowing.** `ZibStack.NET.Log` ships `LogInformation(this ILogger, [InterpolatedStringHandlerArgument("logger")] ref ZibLogInformationHandler)` as an extension method. Once `using ZibStack.NET.Log;` is in scope, C# 11 overload resolution prefers this overload over Microsoft's `LogInformation(this ILogger, string, params object[])` whenever the argument is an interpolated string. The handler itself is a `ref struct` with **typed slots** (`long`, `double`, `decimal`, `string`, `object?`) that store each interpolation argument without boxing, and it captures structured property names via `[CallerArgumentExpression]`. The handler's constructor checks `logger.IsEnabled(…)` and writes `out bool shouldAppend` — if the level is disabled, the compiler skips every `AppendFormatted` call, so `$"{ExpensiveToString()}"` is never evaluated. That gives you lazy eval, zero boxing, and structured properties **from the handler alone**, before any source generator runs.

2. **Source-generated interceptor.** The ZibStack.NET.Log generator scans every `logger.LogXxx($"...")` call site and emits a per-call-site `[InterceptsLocation]` interceptor that dispatches through a **cached** `LoggerMessage.Define<T1, T2, T3>` delegate. Conceptually the generated code is:

   ```csharp
   // One cached delegate per call site, allocated at static init:
   private static readonly Action<ILogger, int, Exception?> __logProcessingOrder =
       LoggerMessage.Define<int>(
           LogLevel.Information,
           new EventId(1, "ProcessingOrder"),
           "Processing order for customer {customerId}");

   // The interceptor your original call site is rewritten to:
   if (!handler.IsEnabled) return;
   __logProcessingOrder(logger, (int)handler.L0, null);
   ```

   The template is parsed exactly once by `LoggerMessage.Define` at static init — not once per call like Microsoft's default path. Combined with the handler's typed slots (zero boxing) and `shouldAppend` (lazy eval), the result is ~5× faster than Microsoft's `LogInformation("template", args)` with zero allocation in both enabled and disabled paths.

Result: structured properties (`customerId=42` as an indexed field in Seq / Elastic / App Insights), lazy evaluation when the level is disabled (~0.4 ns for disabled `LogDebug`), and zero allocation per call.

See [Log → In-depth: how `LogInformation($"...")` actually works](/ZibStack.NET/packages/log/#in-depth-how-loginformation-actually-works) for the full breakdown — including why you can't do it with the handler alone, and what each layer contributes independently.

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

### Built-in `[Metrics]` — RED metrics alongside traces

`[Trace]` produces spans. For RED metrics (rate/errors/duration), add the built-in `[Metrics]` — already registered by `AddAop()`:

```csharp
[Log] [Trace] [Metrics]
public async Task<Order> PlaceOrderAsync(Order o) { ... }
```

This emits three `System.Diagnostics.Metrics` instruments under the `ZibStack.Aop` meter:
- `aop.method.call.count` (Counter) — with tags `aop.class`, `aop.method`
- `aop.method.call.duration` (Histogram, ms) — same tags
- `aop.method.call.errors` (Counter) — same tags

Wire to OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("*").AddOtlpExporter())
    .WithMetrics(metrics => metrics.AddMeter("ZibStack.Aop").AddOtlpExporter());
```

All three aspects (`[Log]`, `[Trace]`, `[Metrics]`) run in a single generated interceptor — no nesting overhead, no reflection.

### Other built-in aspects

`AddAop()` also registers `[Retry]`, `[Cache]`, `[Timeout]`, and `[Authorize]`. See [AOP — Built-in Aspects](/ZibStack.NET/packages/aop/#built-in-aspects) for full reference.

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
