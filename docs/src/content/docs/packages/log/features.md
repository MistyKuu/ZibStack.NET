---
title: Features
description: What the package automatically rewrites — interpolated logs, [Sensitive] masking, [NoLog], scopes, defaults, deferred string allocation.
---

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


