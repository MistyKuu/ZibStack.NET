---
title: "[Log] Attribute"
description: "Structured entry/exit/error logging via the [Log] attribute — generated at compile time by ZibStack.NET.Aop with zero reflection."
---

## `[Log]` Attribute

The `[Log]` attribute adds **structured entry/exit/error logging** to any method or class. It is generated at compile time by the AOP source generator — no reflection, no IL weaving, no runtime proxy.

> **Package:** `ZibStack.NET.Aop` (not `ZibStack.NET.Log`). The Log package provides only interpolated-string logging.

## Quick Start

### 1. Install

```bash
dotnet add package ZibStack.NET.Aop
```

### 2. Wire DI

```csharp
using ZibStack.NET.Aop;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAop();

var app = builder.Build();
app.Services.UseAop();
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

Logger (`ILogger<OrderService>`) is resolved from DI automatically. Every call now logs entry, exit, elapsed time, and exceptions:

```
info: OrderService[1] Entering OrderService.PlaceOrder(customerId: 42, product: Widget, quantity: 3)
info: OrderService[2] Exited OrderService.PlaceOrder in 53ms -> Order { Id = 7 }
```

If the method throws:

```
fail: OrderService[3] OrderService.PlaceOrder failed after 12ms
      System.InvalidOperationException: Out of stock
```

## How It Works

The source generator:

1. Finds classes/methods marked `[Log]`
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

## Sensitive Data Masking

### Parameter masking

```csharp
[Log]
public bool Authenticate(string username, [Sensitive] string password)
{
    // ...
}
// log: Entering AuthService.Authenticate(username: john, password: ***)
```

### Exclude parameters entirely

```csharp
[Log]
public void Upload(string fileName, [NoLog] byte[] fileContent)
{
    // ...
}
// log: Entering StorageService.Upload(fileName: report.pdf)
// fileContent is excluded entirely
```

### Property-level masking

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

### Return value masking

```csharp
[return: Sensitive]  // masks return value as ***
public string GetApiKey() { ... }

[return: NoLog]      // excludes return value from exit logs
public byte[] GetFile() { ... }
```

## Object Logging Mode

Controls how complex objects (classes, records, structs) appear in logs. Configurable per method via `ObjectLogMode`. Primitive types (`int`, `string`, `decimal`, etc.) are always logged directly regardless of the mode.

### Destructure (default)

Serilog-style `{@param}`. Structured logging providers (Serilog, Seq, Application Insights) capture object properties as structured data that you can filter and query by. Console provider falls back to `ToString()`:

```csharp
[Log] // ObjectLogMode.Destructure is the default
public Order GetOrder(int id) { ... }

// Serilog/Seq: captures Result.Id, Result.Product, Result.Total as structured fields
// Console:     Exited GetOrder in 3ms -> SampleApi.Services.Order (ToString fallback)
```

### JSON

Serializes objects with `System.Text.Json`. Works with any logging provider:

```csharp
[Log(ObjectLogging = ObjectLogMode.Json)]
public Order GetOrder(int id) { ... }

// Any provider: Entering GetOrder(id: 1)
//               Exited GetOrder in 3ms -> {"Id":1,"Product":"Widget","Total":29.97}
```

### ToString

Calls `object.ToString()`. Override `ToString()` for custom output:

```csharp
[Log(ObjectLogging = ObjectLogMode.ToString)]
public Order GetOrder(int id) { ... }

// log: Exited GetOrder in 3ms -> SampleApi.Services.Order
// (unless Order overrides ToString())
```

## Custom Log Levels

```csharp
[Log(EntryExitLevel = ZibLogLevel.Debug, ExceptionLevel = ZibLogLevel.Critical)]
public async Task<decimal> CalculateTotalAsync(int orderId)
{
    // ...
}
```

## Minimal Logging (Hot Paths)

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

## Custom Messages

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

## Attribute Properties

| Property | Default | Description |
|---|---|---|
| `EntryExitLevel` | `Information` | Log level for entry/exit (`ZibLogLevel.*`) |
| `ExceptionLevel` | `Error` | Log level for exceptions (`ZibLogLevel.*`) |
| `LogParameters` | `true` | Log parameter values on entry |
| `MeasureElapsed` | `true` | Measure elapsed time with Stopwatch |
| `EntryMessage` | auto | Custom entry message template |
| `ExitMessage` | auto | Custom exit message template |
| `ExceptionMessage` | auto | Custom exception message template |
| `ObjectLogging` | `Destructure` | How complex objects are logged (`ObjectLogMode.*`) |

## Bulk Apply via `IAopConfigurator`

Register `[Log]` on multiple classes without decorating each one:

```csharp
public sealed class AopConfig : IAopConfigurator
{
    public void Configure(IAopBuilder b)
    {
        b.Apply<LogAttribute>(a =>
        {
            a.To("OrderService");
            a.To("PaymentService");
            a.To("ShippingService");
        });
    }
}
```

See [Bulk Apply](/ZibStack.NET/packages/aop/apply/) for full details on `Apply<T>`.

## Project-Wide Defaults via `ILogConfigurator`

Set defaults for all `[Log]` methods in the project. Per-method `[Log(...)]` properties still win. One class per project — the generator discovers it automatically:

```csharp
using ZibStack.NET.Aop;  // ILogConfigurator is in the Aop namespace

public sealed class LogConfig : ILogConfigurator
{
    public void Configure(ILogBuilder b)
    {
        b.Defaults(d =>
        {
            d.EntryExitLevel = ZibLogLevel.Debug;
            d.ObjectLogging = ObjectLogMode.Json;
            d.MeasureElapsed = false;
        });
    }
}
```

| Property | Default | Description |
|---|---|---|
| `EntryExitLevel` | `Information` | Default log level for entry/exit |
| `ExceptionLevel` | `Error` | Default log level for exceptions |
| `LogParameters` | `true` | Log parameter values |
| `LogReturnValue` | `true` | Log return value |
| `MeasureElapsed` | `true` | Measure elapsed time |
| `ObjectLogging` | `Destructure` | How complex objects are logged |

## Async Support

Works with `Task`, `Task<T>`, `ValueTask`, and `ValueTask<T>` — the generator adds `async`/`await` automatically.

```csharp
[Log]
public async Task<List<Order>> GetOrdersAsync(int customerId)
{
    return await _repo.GetByCustomerAsync(customerId);
}
```

## Setup Requirements

- **`UseAop()` is required** — without it, the first call to a `[Log]`-decorated method throws `InvalidOperationException`
- Logger (`ILogger<T>`) is resolved from DI automatically — no manual registration needed
- The interceptor namespace (`ZibStack.Generated`) is added to your project automatically by the package's `build/.props` file

## Diagnostics

| Code | Description |
|---|---|
| SL0005 | `[Log]` on static method (not supported) |

See also: [AOP Analyzers](/ZibStack.NET/packages/aop-analyzers/) for additional compile-time diagnostics.
