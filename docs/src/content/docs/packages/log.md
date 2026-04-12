---
title: ZibStack.NET.Log
description: Lightweight, compile-time logging for .NET 8+ using C# interceptors with zero-allocation logging wrappers and interpolated string support.
---

[![NuGet](https://img.shields.io/nuget/v/ZibStack.NET.Log.svg)](https://www.nuget.org/packages/ZibStack.NET.Log) [![Source](https://img.shields.io/badge/source-GitHub-blue)](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Log)

Lightweight, compile-time logging for .NET 8+ using **C# interceptors**. Add `[Log]` to any method and ZibStack.NET.Log generates zero-allocation logging wrappers automatically — no reflection, no IL weaving, no runtime proxies. Also provides **interpolated string logging** — add `using ZibStack.NET.Log;` and `_logger.LogInformation($"User {user}")` becomes structured at compile time.

> **Quiet by default.** The package doesn't inject a global using and the "use interpolated string" suggestion (`ZLOG002`) is an IDE hint, not a warning. Existing call sites stay untouched until you opt in — see [Configuration](#configuration) below.

> **See the working sample:** [SampleApi on GitHub](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Log/sample/SampleApi)

## Quick Start

### 1. Install

```
dotnet add package ZibStack.NET.Log
```

### 2. Wire DI

```csharp
using ZibStack.NET.Aop;

// Program.cs — wire DI (required):
var app = builder.Build();
app.Services.UseAop();
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
Standard `LogXxx` calls with `$"..."` get structured logging because C# 10+ prefers the interpolated-string handler overload shipped by this package. Add `using ZibStack.NET.Log;` in the file (or enable the global using, see [Configuration](#configuration)):

```csharp
using ZibStack.NET.Log;   // needed for the structured overload to be picked

// Standard ILogger — C# now prefers the structured overload for $"..."
_logger.LogInformation($"User {userId} bought {product} for {total:C}");
// Template: "User {userId} bought {product} for {total:C}"
// Structured properties: userId=42, product="Widget", total=29.97

// Non-interpolated calls still use Microsoft's methods as before:
_logger.LogInformation("Processing complete");
_logger.LogInformation("User {Name}", userName);
```

## Configuration

ZibStack.NET.Log is designed to be a **quiet guest** in consumer projects — no unsolicited global usings, no build warnings on your existing logging code. Two MSBuild properties control the opt-in experience:

| Property | Default | Effect |
|---|---|---|
| `ZibLogEmitGlobalUsing` | `false` | When `true`, the source generator emits `global using ZibStack.NET.Log;` so the interpolated-string handler overload is available everywhere without per-file `using`. |
| `ZibLogStrict` | `false` | When `true`, turns on the opinionated experience: `ZibLogEmitGlobalUsing=true` **and** the `ZLOG002` analyzer is raised from `Info` (IDE hint) to `Warning` (visible in build output). |

### Examples

**New project / greenfield — you want the full experience:**

```xml
<PropertyGroup>
  <ZibLogStrict>true</ZibLogStrict>
</PropertyGroup>
```

Global using on, analyzer shouts at every legacy `LogInformation("template", arg)` call, ready to migrate.

**Large existing codebase — you just want `[Log]` on a few classes without touching everything:**

```xml
<!-- Nothing. Quiet by default. -->
```

Add `using ZibStack.NET.Log;` only in files that use `[Log]` or interpolated-string logging. The `ZLOG002` hint still appears in the IDE as a subtle dot, but won't fail your `TreatWarningsAsErrors` build.

**You want the global using but NOT the warning upgrade:**

```xml
<PropertyGroup>
  <ZibLogEmitGlobalUsing>true</ZibLogEmitGlobalUsing>
</PropertyGroup>
```

**You want the warning upgrade but NOT the global using** (e.g., you prefer explicit imports):

```ini
# .editorconfig
[*.cs]
dotnet_diagnostic.ZLOG002.severity = warning
```

You can always override the analyzer severity per-file or per-folder via `.editorconfig`, regardless of the MSBuild properties.

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

`LogInformation($"...")` vs standard `LogInformation("template", args)` — pure overhead measurement with NullLoggerProvider (no sink, measures handler + dispatch cost only):

| Method | Mean | Allocated |
|---|---:|---:|
| **`LogInformation($"...")` (level OFF)** | **3.2 ns** | **0 B** |
| **`LogInformation($"...")` (level ON)** | **3.8 ns** | **0 B** |
| `LogInformation("template", args)` (level OFF) | 15.7 ns | 104 B |
| `LogInformation("template", args)` (level ON) | 19.1 ns | 104 B |

**Key results:**

1. **~5× faster when the level is disabled.** The interpolated-string handler's constructor checks `IsEnabled` and writes `shouldAppend = false` — the compiler skips every `AppendFormatted` call. Cost: ~3.2 ns vs ~15.7 ns for Microsoft's path (which allocates `params object[]` before the level check). **Zero allocation** in both enabled and disabled paths.
2. **~5× faster when enabled.** With a NullLoggerProvider (logging enabled but output discarded), our interceptor dispatches through a cached `LoggerMessage.Define` delegate at ~3.8 ns / 0 B vs Microsoft's ~19.1 ns / 104 B. The typed-slot handler avoids boxing entirely; Microsoft boxes every argument into `object[]`.

For zero-allocation logging in hot paths, prefer the `[Log]` attribute (~39 ns, 64 B) — it generates a cached `LoggerMessage.Define<T>` delegate per method and bypasses the interpolated string handler entirely.

**How it works:** a two-layer mechanism — see the [In-depth: how `LogInformation($"...")` actually works](#in-depth-how-loginformation-actually-works) section below for the full picture. Short version: a C# 11 interpolated-string handler (`ref struct ZibLogInformationHandler`) shadows Microsoft's `LogXxx(string, params object[])` overload via extension-method resolution and captures arguments into typed slots (zero boxing for primitives, `IsEnabled` checked lazily). The source generator then emits a per-call-site `[InterceptsLocation]` interceptor that reads those slots and dispatches through a **cached** `LoggerMessage.Define<T1, T2, T3>` delegate — one allocation at static init, zero allocations per call.

### Analyzer: detect legacy log calls

The `ZLOG002` analyzer ships inside the main `ZibStack.NET.Log` package — no extra install needed. It warns whenever your code uses the legacy `_logger.LogXxx("template {Param}", value)` form. Only `Microsoft.Extensions.Logging.ILogger` extension calls with a string literal + at least one trailing argument are flagged, so plain messages and already-interpolated calls are left alone.

```csharp
_logger.LogInformation("User {Name}", name);
//      ^^^^^^^^^^^^^^
// ⚠ ZLOG002: Replace 'LogInformation("template", args)' with 'LogInformation($"template")'
//   — the ZibStack.NET.Log generator emits a typed LoggerMessage.Define interceptor
//     (~5x faster, zero allocation vs Microsoft's 104 B)
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

Just call standard `LogXxx` methods — C# 10+ automatically prefers the structured overload for `$"..."` arguments, as long as `ZibStack.NET.Log` is in scope. Add `using ZibStack.NET.Log;` in the file, or enable the global using once via [`<ZibLogEmitGlobalUsing>true</ZibLogEmitGlobalUsing>`](#configuration):

```csharp
using ZibStack.NET.Log;   // needed for the structured overload to be picked

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

**How it works (short):** see the [In-depth mechanism](#in-depth-how-loginformation-actually-works) section below. Two layers work together — the interpolated-string handler does the argument capture and lazy `IsEnabled` check, the source-generated interceptor rewrites the dispatch to use a cached `LoggerMessage.Define` delegate. Disabled log calls cost ~3.2 ns (one `IsEnabled` check via the handler's `out bool shouldAppend`), enabled calls are comparable to hand-written `LoggerMessage.Define`.

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


## In-depth: how `LogInformation($"...")` actually works

There are **two independent questions** about interpolated-string structured logging with ZibStack.NET.Log, and the answer to each one is different:

1. **Why does `logger.LogInformation($"Hey {user}")` preserve structured properties instead of flattening to a string?** → Handled by a C# 11 interpolated-string handler + extension-method shadowing. This is the primary mechanism.
2. **Why is it zero-allocation and ~5× faster than `LogInformation("Hey {user}", user)`?** → Handled by a source-generated interceptor that emits a cached `LoggerMessage.Define<T>` delegate per call site.

You can have the first without the second. You can't have the second without the first. Let's walk through both.

### Layer 1 — extension method shadowing (the primary mechanism)

The package ships a set of extension methods on `ILogger` in `ZibStack.NET.Log`:

```csharp
// ZibStack.NET.Log.Abstractions / LoggerStructuredExtensions.cs
public static void LogInformation(
    this ILogger logger,
    [InterpolatedStringHandlerArgument("logger")]
    ref ZibLogInformationHandler handler)
{ /* body — see Layer 2 */ }
```

When the compiler sees `logger.LogInformation($"Hey {user}")`, overload resolution picks **this** overload over Microsoft's `LoggerExtensions.LogInformation(this ILogger, string, params object[])` — **only because the argument is an interpolated string**. C# 11 preferentially routes interpolated strings to overloads that accept an `[InterpolatedStringHandler]` type when one is in scope. If you passed a plain string (`logger.LogInformation("hello")`), the Microsoft overload still wins.

This is called **extension method shadowing**, and it requires exactly one thing at the call site: **`ZibStack.NET.Log` must be in scope**. Either:

- `using ZibStack.NET.Log;` at the top of the file, or
- `<ZibLogEmitGlobalUsing>true</ZibLogEmitGlobalUsing>` in your `.csproj` (or the `ZibLogStrict` umbrella switch — see [Configuration](#configuration)).

Without the `using`, Microsoft's overload wins and you get the standard flat-string behavior. With the `using`, our overload wins and Layer 2 kicks in.

### What the handler does before anything else runs

`ZibLogInformationHandler` is a `[InterpolatedStringHandler]` `ref struct`. Its constructor takes the logger and writes an `out bool shouldAppend`:

```csharp
public ZibLogInformationHandler(
    int literalLength, int formattedCount, ILogger logger, out bool shouldAppend)
{
    IsEnabled = logger.IsEnabled(LogLevel.Information);
    shouldAppend = IsEnabled;   // ← tells the compiler whether to run AppendFormatted calls
    if (IsEnabled && formattedCount > 6)
        FallbackArgs = new object?[formattedCount];
}
```

If `shouldAppend` is `false`, **the compiler skips every `AppendFormatted` call**. That's a built-in feature of C# 11 interpolated string handlers, not something we wrote. So for a disabled-level call like `logger.LogDebug($"slow string {ExpensiveToString()}")`, `ExpensiveToString()` **is never evaluated** — the compiler emits an `if (shouldAppend) { … }` guard around the whole append block. This is where the ~3.2 ns "disabled path" comes from.

If `shouldAppend` is `true`, the compiler emits one `AppendFormatted` call per interpolation hole. Our handler stores each argument into a **typed slot** matched to its static type:

```csharp
public long L0, L1, L2, L3, L4, L5;        // int / long / bool / byte / short / char / uint
public double D0, D1, D2, D3;               // double / float
public decimal M0, M1;                       // decimal
public string? S0, S1, S2, S3, S4, S5;       // string
public object? O0, O1;                       // custom types (last resort, boxes)

public void AppendFormatted(int v, [CallerArgumentExpression(nameof(v))] string name = "")
    => StoreLong(v);
// … 15+ more overloads per numeric / string / object type
```

Three things to notice:

1. **Zero boxing for primitives.** `int` is stored as `long` in `L0..L5`, not as `object` in an array. Compare with Microsoft's `LogInformation("Hey {user}", int)` which wraps `int` in `object[]` → one boxing per argument.
2. **`CallerArgumentExpression` captures the variable name.** `$"Hey {userId}"` → `AppendFormatted` receives `name = "userId"` for free. That's how the handler knows the structured property name without any runtime reflection — the C# compiler bakes the expression text into the call site.
3. **Format specifiers are preserved.** Each `AppendFormatted` has a `string? format` overload that receives `"C"` for `$"{total:C}"`, `"P2"` for `$"{ratio:P2}"`, etc. — keeping the full template round-trippable.

`AppendLiteral(string s)` is a no-op: we don't need to accumulate the literal text because we're never building a flat string. The literals are re-assembled later from a compile-time template, not from handler state.

**At this point**, whether or not a source generator runs, the handler has everything it needs for structured logging: typed values, structured names, and the format template (conceptually — the template itself is only materialized in Layer 2).

### Layer 2 — source-generated interceptor (the cache optimization)

The handler's extension method has a **no-op body** in the shipped assembly:

```csharp
public static void LogInformation(
    this ILogger logger,
    [InterpolatedStringHandlerArgument("logger")]
    ref ZibLogInformationHandler handler)
{
    // Replaced by generator-emitted [InterceptsLocation] interceptor.
}
```

> **Safety net.** If someone references only `ZibStack.NET.Log.Abstractions` without the generator, the extension method body throws `InvalidOperationException("Install the full ZibStack.NET.Log package")` at the first enabled log call — no silent data loss. In practice the Abstractions package is only referenced transitively by other ZibStack generators; end users always install `ZibStack.NET.Log` which ships both halves.

At compile time, the source generator in `ZibStack.NET.Log` scans every `logger.LogXxx($"...")` call site in your project and emits one interceptor per call site:

```csharp
// Generated file (simplified):
file static class __Interceptors_MyFile
{
    // Cached once at static init — ONE allocation per call site for the whole process
    private static readonly Action<ILogger, int, Exception?> __logger_0 =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(1, "Hey"),
            "Hey {userId}");   // ← literal template, parsed once by LoggerMessage.Define

    [InterceptsLocation(version: 1, "…base64 hash of file+line+column…")]
    public static void __LogInformation_0(
        this ILogger logger,
        ref ZibLogInformationHandler handler)
    {
        if (!handler.IsEnabled) return;
        __logger_0(logger, (int)handler.L0, null);
    }
}
```

`[InterceptsLocation]` tells the compiler: "every time you see a call at the original source location, rewrite it to call this method instead". Your original `logger.LogInformation($"Hey {userId}")` compiles **as if you wrote** `__Interceptors_MyFile.__LogInformation_0(logger, ref handler)`.

What the interceptor adds beyond what the handler already had:

- **Cached `LoggerMessage.Define<T>` delegate** — the template `"Hey {userId}"` is parsed exactly once (at static init) instead of on every call. Microsoft's internal `FormattedLogValues` parses the template on every call into a fresh `LogValuesFormatter` — that's where most of the legacy overhead lives.
- **Typed dispatch** — `__logger_0(logger, (int)handler.L0, null)` is a direct strongly-typed call. No `object[]`, no boxing, no allocation.
- **One delegate per call site**, not per call — the cost is paid once at static init, amortized across every invocation for the process lifetime.

### What you get at each layer

| Property | Microsoft `LogInformation("Hey {x}", x)` | ZibStack handler alone (no interceptor) | ZibStack handler + interceptor |
|---|---|---|---|
| Structured properties preserved | ✓ | ✓ (from handler + `CallerArgumentExpression`) | ✓ |
| Lazy evaluation when disabled | ✓ via `IsEnabled` | ✓ via `shouldAppend` (skips `AppendFormatted`) | ✓ |
| Zero boxing for primitives | ✗ (`object[]`) | ✓ (typed slots) | ✓ |
| Template parsed once, then cached | ✗ (runtime per call) | ✗ | ✓ (`LoggerMessage.Define` at static init) |
| Dispatch allocation per call | `FormattedLogValues` + `object[]` | throws (generator required) | zero |
| Disabled-level cost | ~16 ns | ~3.2 ns (`shouldAppend` check only) | ~3.2 ns |
| Enabled-level cost (NullLogger) | ~19 ns | throws | ~3.8 ns |

Combined result: **~5× faster than Microsoft in both enabled and disabled paths, with zero allocation.** The handler's `shouldAppend` gives us the lazy-eval win; the interceptor's cached delegate gives us the zero-alloc-dispatch win. Microsoft's `LogInformation("template", args)` allocates 104 B (the `params object[]`) even when the level is disabled.

### Why not just use `IFormattedMessage` / handler alone?

The natural instinct is "the handler already has the typed values — why do we need a generator at all? Can't the extension-method body just call `logger.Log(...)` directly?"

It can, but each of the three natural shapes sacrifices something:

1. **Build a flat string and call `logger.Log(level, eventId, formatted, null, (s, _) => s)`** — structured properties are lost. The logger sees `"Hey 42"`, not `{ Template: "Hey {userId}", userId: 42 }`. This is the failure mode the whole library exists to avoid.
2. **Build an `object?[]` from the slots and call `logger.Log(level, eventId, new FormattedLogValues(template, args), …)`** — boxing comes back. The typed-slot win vanishes. Also: `FormattedLogValues` is internal to Microsoft.Extensions.Logging; you can't construct it directly, so you'd call `logger.LogInformation(template, args)` which goes back through the same parse-every-time code path.
3. **Call `LoggerMessage.Define<T>(level, eventId, template)(logger, value, null)` in the extension method body** — this works, but `LoggerMessage.Define` is expensive (it parses the template and builds a `LogValuesFormatter` internally). Doing it on every call is worse than the Microsoft flat path.

The only way to get **all three** of structured + typed + cached is to cache `LoggerMessage.Define` **per call site**, which means you need a separate field per call site, which means you need code generation. That's exactly what the interceptor does — it promotes a single shared extension method into one dedicated method per call site, each with its own cached delegate field.

TL;DR — the handler does 80% of the work. The interceptor is the "pay-once, reuse-forever" cache that gets you the last 20%, and it's particularly visible on the disabled-level path where every nanosecond compounds across hot loops.

## `[Log]` attribute — how that works (different mechanism)

The `[Log]` attribute uses a **completely different** code generation path from the interpolated-string handler — don't mix them up.

ZibStack.NET.Log's `[Log]` generator runs at compile time:

1. Finds classes marked `[Log]` and methods marked `[Log]`
2. For each decorated method, emits an interceptor that wraps the original call with entry/exit/exception logging
3. The wrapper uses `LoggerMessage.Define` (zero-allocation) and resolves the `ILogger<T>` from DI (cached after first resolve)

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

**No reflection. No IL weaving. No runtime proxy. No virtual methods. No partial classes.** The generated code is identical to what you'd write by hand with `LoggerMessage.Define`.

The `[Log]` attribute and interpolated-string logging are orthogonal — you can use either, both, or neither in the same project. They share only the `LoggerMessage.Define` machinery they both emit.

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
