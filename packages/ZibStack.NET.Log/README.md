# ZibStack.NET.Log

Compile-time logging utilities for .NET 8+ using C# interceptors.

## Two features

| Feature | What it does | Requires |
|---|---|---|
| `[Log]` attribute | Structured entry/exit/error logging on methods | `ZibStack.NET.Aop` (handles generation) |
| Interpolated string interception | Rewrites `logger.LogInformation($"...")` into zero-allocation `LoggerMessage.Define` | This package alone |

## Install

```
dotnet add package ZibStack.NET.Aop   # [Log] attribute + Apply<LogAttribute>
dotnet add package ZibStack.NET.Log   # interpolated-string optimization (optional)
```

## [Log] attribute (via ZibStack.NET.Aop)

```csharp
public class OrderService
{
    [Log]
    public Order PlaceOrder(int customerId, string product, int quantity)
    {
        return _repo.Create(customerId, product, quantity);
    }
}

// Output:
// info: OrderService Entering PlaceOrder(customerId: 42, product: Widget, quantity: 3)
// info: OrderService Exited PlaceOrder in 53ms -> Order { Id = 7 }
```

Or apply globally without any attributes:

```csharp
public sealed class AopConfig : IAopConfigurator
{
    public void Configure(IAopBuilder b)
    {
        b.Apply<LogAttribute>(to => to
            .Namespace("MyApp.Services")
            .PublicMethods()
        );
    }
}
```

## Interpolated-string logging (this package)

```csharp
// Before (allocates string + params array every call):
logger.LogInformation($"Processing order {orderId} for customer {name}");

// After (rewritten at compile time to LoggerMessage.Define — zero allocation):
// Same source code, zero changes needed. Just install ZibStack.NET.Log.
```

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/log/](https://mistykuu.github.io/ZibStack.NET/packages/log/)
