---
title: ZibStack.NET.Log
description: Lightweight, compile-time logging for .NET 8+ using C# interceptors with zero-allocation logging wrappers and interpolated string support.
---

Lightweight, compile-time logging for .NET 8+ using **C# interceptors**. Add `[Log]` to any method and ZibStack.NET.Log generates zero-allocation logging wrappers automatically — no reflection, no IL weaving, no runtime proxies. Also provides **interpolated string logging** (`LogInformationEx($"...")`) with structured logging support.

## Quick Start

### 1. Install

```
dotnet add package ZibStack.NET.Log
```

### 2. Enable interceptors + wire DI

```xml
<!-- .csproj -->
<PropertyGroup>
    <InterceptorsPreviewNamespaces>$(InterceptorsPreviewNamespaces);ZibStack.Generated</InterceptorsPreviewNamespaces>
</PropertyGroup>
```

```csharp
// Program.cs — wire DI (required):
var app = builder.Build();
AspectServiceProvider.ServiceProvider = app.Services;
```

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

Use `$"..."` with structured logging — no more `"template {Param}", value` boilerplate:

```csharp
_logger.LogInformationEx($"User {userId} bought {product} for {total:C}");
// Structured properties: userId=42, product="Widget", total=29.97
```

## Benchmarks

Overhead of calling `int Add(int a, int b) => a + b;` with logging, BenchmarkDotNet on .NET 10.0:

| Method | Mean | Allocated |
|---|---:|---:|
| No logging (baseline) | 0.2 ns | 0 B |
| Manual `LoggerMessage.Define` (level OFF) | 34.3 ns | 0 B |
| Manual `LoggerMessage.Define` | 39.2 ns | 0 B |
| **ZibStack.Log `[Log]` no stopwatch** | **42.9 ns** | **64 B** |
| **ZibStack.Log `[Log]` (level OFF)** | **45.3 ns** | **64 B** |
| **ZibStack.Log `[Log]`** | **46.1 ns** | **64 B** |
| `[Log]` return object (no `[Sensitive]`) | 49.1 ns | 96 B |
| Manual `ILogger.Log()` (level OFF) | 73.2 ns | 176 B |
| Manual `ILogger.Log()` | 94.1 ns | 176 B |
| `[Log]` return object (with `[Sensitive]`) | 116.1 ns | 624 B |

- **ZibStack.Log ≈ hand-written `LoggerMessage.Define`** — same tier (~46 ns vs ~39 ns)
- **2x faster** than `_logger.LogInformation()` (46 ns vs 94 ns)
- **2.8x less memory** than `_logger.LogInformation()` (64 B vs 176 B)
- When log level OFF: ~45 ns (just DI resolve + `IsEnabled` check)

### Property-level sanitization overhead

When return type has `[Sensitive]`/`[NoLog]` properties (Dictionary + JSON serialization):

| Method | Mean | Allocated |
|---|---:|---:|
| `[Log]` return object (no `[Sensitive]`) | 49.1 ns | 96 B |
| `[Log]` return object (with `[Sensitive]`) | 116.1 ns | 624 B |

+67 ns and +528 B per call for sanitization. Use `[Sensitive]` on properties only where needed.

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

Use `$"..."` interpolated strings with structured logging — no more `"template {Param}", value` boilerplate. Variable names are automatically captured as property names via `CallerArgumentExpression`:

```csharp
var userId = 42;
var product = "Widget";
var total = 29.97m;

_logger.LogInformationEx($"User {userId} bought {product} for {total:C}");
// Template: "User {userId} bought {product} for {total:C}"
// Structured properties: userId=42, product="Widget", total=29.97

_logger.LogWarningEx($"Low stock for {product}");
_logger.LogErrorEx(ex, $"Failed to process order for {userId}");
_logger.LogDebugEx($"Cache hit ratio: {ratio:P2}");
```

Available methods: `LogTraceEx`, `LogDebugEx`, `LogInformationEx`, `LogWarningEx`, `LogErrorEx`, `LogCriticalEx`. Error/Critical variants also accept an `Exception` parameter.

> Expressions like `user.Name` are sanitized to valid property names: `userName`.

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

| Method | Description |
|---|---|
| `_logger.LogTraceEx($"...")` | Trace level with structured properties |
| `_logger.LogDebugEx($"...")` | Debug level |
| `_logger.LogInformationEx($"...")` | Information level |
| `_logger.LogWarningEx($"...")` | Warning level |
| `_logger.LogErrorEx($"...")` | Error level |
| `_logger.LogErrorEx(ex, $"...")` | Error level with exception |
| `_logger.LogCriticalEx($"...")` | Critical level |
| `_logger.LogCriticalEx(ex, $"...")` | Critical level with exception |

## Requirements

- **.NET 8.0** or later (uses interceptors)
- `<InterceptorsPreviewNamespaces>ZibStack.Generated</InterceptorsPreviewNamespaces>` in `.csproj`

## Roadmap

No open items. Have a feature request? [Open an issue](https://github.com/MistyKuu/ZibStack.NET.Log/issues).

## License

MIT
