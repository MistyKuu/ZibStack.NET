---
title: ZibStack.NET.Log
description: Lightweight, compile-time logging for .NET 8+ using C# interceptors with zero-allocation logging wrappers and interpolated string support.
---

[![NuGet](https://img.shields.io/nuget/v/ZibStack.NET.Log.svg)](https://www.nuget.org/packages/ZibStack.NET.Log) [![Source](https://img.shields.io/badge/source-GitHub-blue)](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Log)

Lightweight, compile-time logging for .NET 8+ using **C# interceptors**. Add `[Log]` to any method and ZibStack.NET.Log generates zero-allocation logging wrappers automatically — no reflection, no IL weaving, no runtime proxies. Also provides **interpolated string logging** — just write `_logger.LogInformation($"User {user}")` and get structured logging automatically (with `using ZibStack.NET.Log;`).

> **See the working sample:** [SampleApi on GitHub](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Log/sample/SampleApi)

## Quick Start

### 1. Install

```
dotnet add package ZibStack.NET.Log
```

### 2. Wire DI

```csharp
// Program.cs — wire DI (required):
var app = builder.Build();
AspectServiceProvider.ServiceProvider = app.Services;
```

> Interceptor namespaces (`ZibStack.Generated`, `ZibStack.Generated.Log`) are added to your project automatically by the package's `build/.props` file on restore. No manual `<InterceptorsNamespaces>` edit needed.

### 3. Add `[Log]`

```csharp
// On a method:
public class OrderService
{
    [Log]
    public Order PlaceOrder(int customerId, string product, int quantity)
    {
        return _repo.Create(customerId, product, quantity);
    }
}

// Or on a class — logs ALL public methods:
[Log]
public class OrderService
{
    public Order PlaceOrder(int id) { ... }     // logged
    public void Ping() { ... }                  // logged
    private void Internal() { ... }             // NOT logged
}
```

Logger (`ILogger<OrderService>`) is resolved from DI automatically. Every call to `PlaceOrder` now logs entry, exit, elapsed time, and exceptions:

```
info: OrderService[1] Entering OrderService.PlaceOrder(customerId: 42, product: Widget, quantity: 3)
info: OrderService[2] Exited OrderService.PlaceOrder in 53ms -> Order { Id = 7 }
```

If the method throws:

```
fail: OrderService[3] OrderService.PlaceOrder failed after 12ms
      System.InvalidOperationException: Out of stock
```

### Interpolated string logging

Use `$"..."` with structured logging — no more `"template {Param}", value` boilerplate.
Standard `LogXxx` calls with `$"..."` get structured logging automatically — C# 10+ prefers the handler overload for interpolated strings (the `using ZibStack.NET.Log` is added globally by the generator):

```csharp
// Standard ILogger — C# automatically picks the structured overload for $"..."
_logger.LogInformation($"User {userId} bought {product} for {total:C}");
// Template: "User {userId} bought {product} for {total:C}"
// Structured properties: userId=42, product="Widget", total=29.97

// Non-interpolated calls still use Microsoft's methods as before:
_logger.LogInformation("Processing complete");
_logger.LogInformation("User {Name}", userName);
```

## Benchmarks

Overhead of calling `int Add(int a, int b) => a + b;` with logging, BenchmarkDotNet on .NET 10.0:

| Method | Mean | Allocated |
|---|---:|---:|
| No logging (baseline) | 0.0 ns | 0 B |
| Manual `LoggerMessage.Define` (level OFF) | 32.3 ns | 0 B |
| Manual `LoggerMessage.Define` | 34.7 ns | 0 B |
| **ZibStack.Log `[Log]` no stopwatch** | **34.9 ns** | **64 B** |
| **ZibStack.Log `[Log]` (level OFF)** | **38.3 ns** | **64 B** |
| **ZibStack.Log `[Log]`** | **38.8 ns** | **64 B** |
| `[Log]` return object (no `[Sensitive]`) | 40.4 ns | 96 B |
| Manual `ILogger.Log()` (level OFF) | 62.9 ns | 176 B |
| Manual `ILogger.Log()` | 68.6 ns | 176 B |
| `[Log]` return object (with `[Sensitive]`) | 96.8 ns | 624 B |

- **ZibStack.Log ≈ hand-written `LoggerMessage.Define`** — same tier (~39 ns vs ~35 ns)
- **~2x faster** than `_logger.LogInformation()` (39 ns vs 69 ns)
- **2.8x less memory** than `_logger.LogInformation()` (64 B vs 176 B)
- When log level OFF: ~38 ns (just DI resolve + `IsEnabled` check)

### Property-level sanitization overhead

When return type has `[Sensitive]`/`[NoLog]` properties (Dictionary + JSON serialization):

| Method | Mean | Allocated |
|---|---:|---:|
| `[Log]` return object (no `[Sensitive]`) | 40.4 ns | 96 B |
| `[Log]` return object (with `[Sensitive]`) | 96.8 ns | 624 B |

+56 ns and +528 B per call for sanitization. Use `[Sensitive]` on properties only where needed.

### Interpolated string logging

`LogInformation($"...")` vs standard `LogInformation("template", args)` — measured against a real **Serilog** sink (`Serilog.Sinks.InMemory`) so the numbers reflect production cost, not JIT escape-analysis tricks.

| Method | Mean | Allocated |
|---|---:|---:|
| **`LogInformation($"...")` (level OFF)** | **0.4 ns** | **0 B** |
| `LogInformation("template", args)` (level OFF) | 15.8 ns | 104 B |
| `LogInformation("template", args)` (standard, no sink) | 18.6 ns | 104 B |
| `LogInformation($"...")` (no sink) | 1.3 ns | 0 B |
| `REAL: LogInformation("template", args)` (Serilog) | 924 ns | 752 B |
| `REAL: LogInformation($"...")` structured (Serilog) | 989 ns | 784 B |

**Two key results:**

1. **~40x faster when the level is disabled.** The source-generated interceptor checks `IsEnabled` before evaluating the interpolated string, so disabled log calls cost ~0.4 ns vs ~16 ns for the standard API. Hot loops with debug logging behind a level check pay almost nothing.
2. **Comparable production cost when enabled.** With a real Serilog sink, our interpolated form costs ~989 ns vs ~924 ns for the Microsoft form — a **+7% overhead** for the natural `$"..."` syntax. The bulk of the time (~870 ns) is spent inside Serilog's `LogEvent` construction, not in our code, so the handler/interceptor overhead is negligible.

For zero-allocation logging in hot paths, prefer the `[Log]` attribute (~39 ns, 64 B) — it generates a cached `LoggerMessage.Define<T>` delegate per method and bypasses the interpolated string handler entirely.

**How it works:** the source generator scans every `logger.LogXxx($"...")` call site, parses the interpolated string at compile time, and emits a `[InterceptsLocation]` interceptor that creates a cached `LoggerMessage.Define<T1, T2, T3>` delegate with the literal template string. No template parsing at runtime, no `StringBuilder`, no boxing in your code.

### Analyzer: detect legacy log calls

The `ZLOG002` analyzer ships inside the main `ZibStack.NET.Log` package — no extra install needed. It warns whenever your code uses the legacy `_logger.LogXxx("template {Param}", value)` form. Only `Microsoft.Extensions.Logging.ILogger` extension calls with a string literal + at least one trailing argument are flagged, so plain messages and already-interpolated calls are left alone.

```csharp
_logger.LogInformation("User {Name}", name);
//      ^^^^^^^^^^^^^^
// ⚠ ZLOG002: Replace 'LogInformation("template", args)' with 'LogInformation($"template")'
//   — the ZibStack.NET.Log generator emits a typed LoggerMessage.Define interceptor
//     (same alloc, ~40x faster when level is disabled)
```

## Features

### Sensitive Data Masking

```csharp
[Log]
public bool Authenticate(string username, [Sensitive] string password)
{
    // ...
}
// log: Entering AuthService.Authenticate(username: john, password: ***)
```

### Property-Level Masking

`[Sensitive]` and `[NoLog]` work on class properties too — nested objects are handled recursively:

```csharp
public class Order
{
    public int Id { get; set; }
    public string Product { get; set; }
    [Sensitive] public string CreditCard { get; set; }  // → "***"
    [NoLog] public byte[] RawPayload { get; set; }      // excluded
    public CustomerInfo Customer { get; set; }           // nested
}

public class CustomerInfo
{
    public string Name { get; set; }
    [Sensitive] public string Email { get; set; }        // → "***" (nested)
}

[Log]
public Order PlaceOrder(Order order) { ... }
// log: {"Id":1,"Product":"Widget","CreditCard":"***","Customer":{"Name":"John","Email":"***"}}
```

### Exclude Parameters

```csharp
[Log]
public void Upload(string fileName, [NoLog] byte[] fileContent)
{
    // ...
}
// log: Entering StorageService.Upload(fileName: report.pdf)
// fileContent is excluded entirely
```

### Custom Log Levels

```csharp
[Log(EntryExitLevel = ZibLogLevel.Debug, ExceptionLevel = ZibLogLevel.Critical)]
public async Task<decimal> CalculateTotalAsync(int orderId)
{
    // ...
}
```

### Minimal Logging (Hot Paths)

```csharp
[Log(LogParameters = false, MeasureElapsed = false)]
[return: NoLog]
public void Ping()
{
    // ...
}
// log: Entering HealthService.Ping()
// log: Exited HealthService.Ping
```

### Custom Messages

```csharp
[Log(
    EntryMessage = "Processing payment for order {orderId}, amount: {amount}",
    ExitMessage = "Payment completed in {ElapsedMs}ms -> {Result}")]
public Task<Receipt> ProcessPaymentAsync(int orderId, decimal amount)
{
    // ...
}
```

**Available placeholders:**

| Placeholder | Where | Description |
|---|---|---|
| `{paramName}` | `EntryMessage` | Method parameter by name (e.g. `{orderId}`, `{amount}`) |
| `{ElapsedMs}` | `ExitMessage` | Elapsed time in ms (requires `MeasureElapsed = true`) |
| `{Result}` | `ExitMessage` | Return value as string (excluded by `[return: NoLog]`) |

Parameters marked `[Sensitive]` are logged as `***`. Parameters marked `[NoLog]` are excluded entirely. Use `[return: Sensitive]` / `[return: NoLog]` for return values.

### Object Logging

Controls how complex objects (classes, records, structs) appear in logs. Configurable per method via `ObjectLogMode`. Primitive types (`int`, `string`, `decimal`, etc.) are always logged directly regardless of the mode.

**Destructure** (default) — Serilog-style `{@param}`. Structured logging providers (Serilog, Seq, Application Insights) capture object properties as structured data that you can filter and query by. Console provider falls back to `ToString()`:

```csharp
[Log] // ObjectLogMode.Destructure is the default
public Order GetOrder(int id) { ... }

// Serilog/Seq: captures Result.Id, Result.Product, Result.Total as structured fields
// Console:     Exited GetOrder in 3ms -> SampleApi.Services.Order (ToString fallback)
```

**JSON** — serializes objects with `System.Text.Json`. Works with any logging provider:

```csharp
[Log(ObjectLogging = ObjectLogMode.Json)]
public Order GetOrder(int id) { ... }

// Any provider: Entering GetOrder(id: 1)
//               Exited GetOrder in 3ms -> {"Id":1,"Product":"Widget","Total":29.97}
```

**ToString** — calls `object.ToString()`. Override `ToString()` for custom output:

```csharp
[Log(ObjectLogging = ObjectLogMode.ToString)]
public Order GetOrder(int id) { ... }

// log: Exited GetOrder in 3ms -> SampleApi.Services.Order
// (unless Order overrides ToString())
```

### Interpolated String Logging

Use `$"..."` interpolated strings with structured logging — no more `"template {Param}", value` boilerplate. Variable names are automatically captured as property names via `CallerArgumentExpression`.

Just call standard `LogXxx` methods — C# 10+ automatically prefers the structured overload for `$"..."` arguments. The `using ZibStack.NET.Log` is added globally by the source generator, so no imports are required:

```csharp
var userId = 42;
var product = "Widget";
var total = 29.97m;

// Standard ILogger calls — structured logging works automatically:
_logger.LogInformation($"User {userId} bought {product} for {total:C}");
// Template: "User {userId} bought {product} for {total:C}"
// Structured properties: userId=42, product="Widget", total=29.97

_logger.LogWarning($"Low stock for {product}");
_logger.LogError(ex, $"Failed to process order for {userId}");
_logger.LogDebug($"Cache hit ratio: {ratio:P2}");

// Non-interpolated calls still use Microsoft's methods as before:
_logger.LogInformation("Processing complete");
_logger.LogInformation("User {Name}", userName);
```

Available methods: `LogTrace`, `LogDebug`, `LogInformation`, `LogWarning`, `LogError`, `LogCritical`. All accept an optional `Exception` parameter.

> Expressions like `user.Name` are sanitized to valid property names: `userName`.

**How it works:** The source generator scans every `logger.LogXxx($"...")` call site at compile time, parses the interpolated string from the syntax tree, and emits a `[InterceptsLocation]` interceptor. The interceptor:

1. Creates a cached `LoggerMessage.Define<T1, T2, T3>` delegate with the literal template (one per call site, allocated once)
2. Reads typed slots (`L0`, `D0`, `M0`, `S0`...) from the handler — no `object[]`, no template parsing at runtime
3. Skips evaluation entirely when the level is disabled (one `IsEnabled` check, ~0.4 ns)

This makes disabled log calls effectively free (40x faster than the standard `LogInformation("template", args)`) and enabled calls comparable to hand-written `LoggerMessage.Define` code.

### Structured Exceptions

`ZibException` preserves structured logging data from interpolated strings. When caught and logged, the template and individual properties are available for structured logging sinks:

```csharp
// Throw with interpolated string — template + properties captured automatically:
throw new ZibException($"Order {orderId} not found for user {userId}");
// Message: "Order 123 not found for user 42"
// Template: "Order {orderId} not found for user {userId}"
// Properties: { orderId: 123, userId: 42 }

// When catching — log with structured data preserved:
catch (ZibException ex)
{
    ex.LogTo(logger, LogLevel.Error);
    // Structured log: "Order {orderId} not found for user {userId}" with orderId=123, userId=42
}

// Or use the generic extension for any exception (falls back to standard for non-ZibException):
catch (Exception ex)
{
    logger.LogException(ex, LogLevel.Error);
}
```

Typed variant with domain-specific error codes:

```csharp
public enum OrderError { NotFound, OutOfStock, InvalidQuantity }

throw new ZibException<OrderError>(OrderError.NotFound, $"Order {orderId} not found");
// ex.Code == OrderError.NotFound
```

### Async Support

Works with `Task`, `Task<T>`, `ValueTask`, and `ValueTask<T>` — the generator adds `async`/`await` automatically.

```csharp
[Log]
public async Task<List<Order>> GetOrdersAsync(int customerId)
{
    return await _repo.GetByCustomerAsync(customerId);
}
```


## How It Works

ZibStack.NET.Log is a **Roslyn source generator** that runs at compile time:

1. Finds classes marked with `[ZibStack.Log]` and methods marked with `[Log]`
2. Scans all call-sites in your project where those methods are invoked
3. Generates **C# interceptors** (`[InterceptsLocation]`) that redirect each call-site to a logging wrapper
4. The wrapper uses `LoggerMessage.Define` (zero-allocation) and DI-resolved logger (cached after first call)

```
Your code                          Generated interceptor
──────────                         ─────────────────────
service.PlaceOrder(42, "Widget")   →   PlaceOrder_ZibStack.Log(@this, 42, "Widget")
                                       {
                                           log entry (LoggerMessage.Define)
                                           stopwatch.Start()
                                           try {
                                               result = @this.PlaceOrder(42, "Widget")
                                               log exit + elapsed
                                           } catch {
                                               log error + elapsed
                                               throw
                                           }
                                       }
```

**No reflection. No IL weaving. No runtime proxy. No virtual methods. No partial classes.**

The generated code is identical to what you'd write by hand with `LoggerMessage.Define`.

## Diagnostics

ZibStack.NET.Log reports clear compiler errors when something is misconfigured:

| Code | Description |
|---|---|
| SL0005 | `[Log]` on static method (not supported) |

## Attribute Reference

### Automatic method/class logging (`[Log]`)

| Attribute | Target | Default | Description |
|---|---|---|---|
| `[Log]` | Method | | Adds entry/exit/exception logging |
| `[Log(EntryExitLevel = ...)]` | Method | `Information` | Log level for entry/exit (`ZibLogLevel.*`) |
| `[Log(ExceptionLevel = ...)]` | Method | `Error` | Log level for exceptions (`ZibLogLevel.*`) |
| `[Log(LogParameters = false)]` | Method | `true` | Log parameter values on entry |
| `[Log(MeasureElapsed = false)]` | Method | `true` | Measure elapsed time with Stopwatch |
| `[Log(EntryMessage = "...")]` | Method | auto | Custom entry message template |
| `[Log(ExitMessage = "...")]` | Method | auto | Custom exit message template |
| `[Log(ExceptionMessage = "...")]` | Method | auto | Custom exception message template |
| `[Log(ObjectLogging = ...)]` | Method | `Destructure` | How complex objects are logged (`ObjectLogMode.*`) |
| `[Sensitive]` | Parameter | | Masks value as `***` in logs |
| `[return: Sensitive]` | Return value | | Masks return value as `***` in exit logs |
| `[NoLog]` | Parameter | | Excludes parameter from logs entirely |
| `[return: NoLog]` | Return value | | Excludes return value from exit logs |

### Assembly-level defaults

Set defaults for all `[Log]` methods in the assembly. Per-method properties override these.

```csharp
[assembly: ZibLogDefaults(
    EntryExitLevel = ZibLogLevel.Debug,
    ObjectLogging = ObjectLogMode.Json,
    MeasureElapsed = false)]
```

| Property | Default | Description |
|---|---|---|
| `EntryExitLevel` | `Information` | Default log level for entry/exit |
| `ExceptionLevel` | `Error` | Default log level for exceptions |
| `LogParameters` | `true` | Log parameter values |
| `LogReturnValue` | `true` | Log return value |
| `MeasureElapsed` | `true` | Measure elapsed time |
| `ObjectLogging` | `Destructure` | How complex objects are logged |

### Interpolated string logging

With `using ZibStack.NET.Log;`, standard `LogXxx` methods accept `$"..."` with structured logging:

| Method | Description |
|---|---|
| `_logger.LogTrace($"...")` | Trace level with structured properties |
| `_logger.LogDebug($"...")` | Debug level |
| `_logger.LogInformation($"...")` | Information level |
| `_logger.LogWarning($"...")` | Warning level |
| `_logger.LogError($"...")` | Error level |
| `_logger.LogError(ex, $"...")` | Error level with exception |
| `_logger.LogCritical($"...")` | Critical level |
| `_logger.LogCritical(ex, $"...")` | Critical level with exception |


## Requirements

- **.NET 8.0** or later (uses C# interceptors)

That's it. The package's `build/.props` enables `InterceptorsNamespaces` automatically — you don't need to touch your `.csproj`.

## Roadmap

No open items. Have a feature request? [Open an issue](https://github.com/MistyKuu/ZibStack.NET.Log/issues).

## License

MIT
