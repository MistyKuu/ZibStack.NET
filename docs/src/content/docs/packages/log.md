---
title: ZibStack.NET.Log
description: Zero-allocation interpolated-string logging for .NET 8+ — structured $"..." logging via C# interceptors with no code changes to ILogger calls.
---

[![NuGet](https://img.shields.io/nuget/v/ZibStack.NET.Log.svg)](https://www.nuget.org/packages/ZibStack.NET.Log) [![Source](https://img.shields.io/badge/source-GitHub-blue)](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Log)

Zero-allocation **interpolated-string logging** for .NET 8+ using **C# interceptors**. Use `$"..."` with `ILogger` and get structured, zero-allocation log calls at compile time.

> **Looking for `[Log]` attribute (entry/exit/error logging)?** That feature is now part of the **[AOP package](/ZibStack.NET/packages/aop/log-attribute/)**. Install `ZibStack.NET.Aop` for `[Log]`.

> **Quiet by default.** The package doesn't inject a global using and the "use interpolated string" suggestion (`ZLOG002`) is an IDE hint, not a warning. Existing call sites stay untouched until you opt in — see [Configuration](#configuration) below.

> **See the working sample:** [SampleApi on GitHub](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Log/sample/SampleApi)

## Quick Start

### 1. Install

```
dotnet add package ZibStack.NET.Log
```

### 2. Use interpolated strings with ILogger

Add `using ZibStack.NET.Log;` in the file (or enable the global using — see [Configuration](#configuration)):

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

Available methods: `LogTrace`, `LogDebug`, `LogInformation`, `LogWarning`, `LogError`, `LogCritical`. All accept an optional `Exception` parameter.

### Custom property names (`#` override)

By default, the structured property name comes from `CallerArgumentExpression` — the variable name in your code. Override it with `#name` in the format specifier:

```csharp
var result = GetOrderId();

// Default — property name is "result" (from CallerArgumentExpression):
_logger.LogInformation($"Order {result} processed");
// Template: "Order {result} processed"

// Override — property name is "orderId":
_logger.LogInformation($"Order {result:#orderId} processed");
// Template: "Order {orderId} processed"

// With format specifier + override:
_logger.LogInformation($"Total: {total:C#orderTotal}");
// Template: "Total: {orderTotal:C}"
```

Useful when:
- Two log calls use the same variable name (`result`) but mean different things — Elastic/Kibana creates one field per name, so type mismatches on `result` cause log rejection
- The variable name is unhelpful (`x`, `tmp`, `item`)
- You want stable property names that survive variable renames

The `#` override is resolved at **compile time** by the source generator — zero runtime cost.

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

## Configuration

ZibStack.NET.Log is designed to be a **quiet guest** in consumer projects — no unsolicited global usings, no build warnings on your existing logging code. Two MSBuild properties control the opt-in experience:

| Property | Default | Effect |
|---|---|---|
| `ZibLogEmitGlobalUsing` | `false` | When `true`, the source generator emits `global using ZibStack.NET.Log;` so the interpolated-string handler overload is available everywhere without per-file `using`. |
| `ZibLogStrict` | `false` | When `true`, turns on the opinionated experience: `ZibLogEmitGlobalUsing=true` **and** the `ZLOG002` analyzer is raised from `Info` (IDE hint) to `Warning` (visible in build output). |

Project-wide interpolation settings go through a fluent `ILogConfigurator` implementation:

| Property | Default | Effect |
|---|---|---|
| `PropertyNameCasing` | `PascalCase` (0) | `$"{userId}"` → template `{UserId}`. Matches Serilog/Seq/Elastic convention. |
| | `CamelCase` (1) | `$"{userId}"` → template `{userId}`. Keeps the variable name as-is. |

```csharp
// Default (PascalCase — no configurator needed):
_logger.LogInformation($"User {userId} bought {product}");
// Template: "User {UserId} bought {Product}"

// Opt into camelCase — one class per project, parsed at compile time:
public sealed class LogConfig : ILogConfigurator
{
    public void Configure(ILogBuilder b) => b.Interpolation(i =>
    {
        i.PropertyNameCasing = ZibLogPropertyCasing.CamelCase;
    });
}
// Template: "User {userId} bought {product}"
```

### Examples

**New project / greenfield — you want the full experience:**

```xml
<PropertyGroup>
  <ZibLogStrict>true</ZibLogStrict>
</PropertyGroup>
```

Global using on, analyzer shouts at every legacy `LogInformation("template", arg)` call, ready to migrate.

**Large existing codebase — you just want interpolated logging on a few files:**

```xml
<!-- Nothing. Quiet by default. -->
```

Add `using ZibStack.NET.Log;` only in files that use interpolated-string logging. The `ZLOG002` hint still appears in the IDE as a subtle dot, but won't fail your `TreatWarningsAsErrors` build.

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

## How It Works

Two layers work together:

1. **Interpolated-string handler** (`ref struct ZibLogInformationHandler`) — shadows Microsoft's `LogXxx(string, params object[])` overload via extension-method resolution. Captures arguments into typed slots (zero boxing for primitives). The handler constructor checks `IsEnabled` — if the level is disabled, the compiler skips every `AppendFormatted` call (~3.2 ns, zero allocation).

2. **Source-generated interceptor** — emits a per-call-site `[InterceptsLocation]` interceptor that reads the handler's slots and dispatches through a **cached** `LoggerMessage.Define<T1, T2, T3>` delegate. One allocation at static init, zero allocations per call.

> Expressions like `user.Name` are sanitized to valid property names: `userName`.

## Read more

- [Features](/ZibStack.NET/packages/log/features/) — interpolated rewriting, `[Sensitive]` masking, `[NoLog]`, scopes, defaults.
- [How interpolated logs are rewritten](/ZibStack.NET/packages/log/internals/) — internals: handler shadowing + source-gen interceptor.
- [Benchmarks](/ZibStack.NET/packages/log/benchmarks/) — cost vs hand-written `ILogger` calls.
- [`[Log]` attribute](/ZibStack.NET/packages/aop/log-attribute/) — method-level entry/exit logging (AOP package).
- [Attribute reference](/ZibStack.NET/packages/log/attributes/) — full parameter list.

## Roadmap & License

MIT. .NET Standard 2.0+ generator, runs in any C# 12+ project.
