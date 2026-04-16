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

Project-wide settings (including property-name casing and `[Log]` defaults) go through a fluent `ILogConfigurator` implementation:

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


## Read more

- [Features](/ZibStack.NET/packages/log/features/) — interpolated rewriting, `[Sensitive]` masking, `[NoLog]`, scopes, defaults.
- [How interpolated logs are rewritten](/ZibStack.NET/packages/log/internals/) — internals: handler shadowing + source-gen interceptor.
- [`[Log]` attribute & diagnostics](/ZibStack.NET/packages/log/log-attribute/) — method-level entry/exit logging.
- [Attribute reference](/ZibStack.NET/packages/log/attributes/) — full parameter list.
- [Benchmarks](/ZibStack.NET/packages/log/benchmarks/) — cost vs hand-written `ILogger` calls.

## Roadmap & License

MIT. .NET Standard 2.0+ generator, runs in any C# 12+ project.
