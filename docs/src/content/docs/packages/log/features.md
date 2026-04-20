---
title: Features
description: What the package automatically rewrites — interpolated logs, [Sensitive] masking, custom property names, structured exceptions.
---

## Features

### Interpolated String Logging

Use `$"..."` interpolated strings with structured logging — no more `"template {Param}", value` boilerplate. Variable names are automatically captured as property names via `CallerArgumentExpression`.

Just call standard `LogXxx` methods — C# 10+ automatically prefers the structured overload for `$"..."` arguments, as long as `ZibStack.NET.Log` is in scope. Add `using ZibStack.NET.Log;` in the file, or enable the global using once via [`<ZibLogEmitGlobalUsing>true</ZibLogEmitGlobalUsing>`](/ZibStack.NET/packages/log/#configuration):

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

The `#` override is resolved at **compile time** by the source generator — zero runtime cost. Everything before `#` is the format specifier, everything after is the property name. No `#` = default `CallerArgumentExpression` behavior.

**How it works (short):** Two layers work together — the interpolated-string handler does the argument capture and lazy `IsEnabled` check, the source-generated interceptor rewrites the dispatch to use a cached `LoggerMessage.Define` delegate. Disabled log calls cost ~3.2 ns (one `IsEnabled` check via the handler's `out bool shouldAppend`), enabled calls are comparable to hand-written `LoggerMessage.Define`.

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

### `[Log]` Attribute Features

The `[Log]` attribute (entry/exit/error logging, `[Sensitive]`/`[NoLog]` masking, `ObjectLogMode`, custom levels) is now part of the **AOP** package.

See: **[AOP — Log Attribute](/ZibStack.NET/packages/aop/log-attribute/)** for:
- Sensitive data masking (`[Sensitive]`, `[NoLog]`)
- Property-level masking (nested objects)
- Custom log levels (`EntryExitLevel`, `ExceptionLevel`)
- Minimal logging for hot paths
- Custom messages with placeholders
- Object logging modes (Destructure/Json/ToString)
- Async support
