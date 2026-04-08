# ZibStack.NET.Log

Compile-time logging for .NET 8+ using C# interceptors — add `[Log]` to any method and get zero-allocation logging wrappers automatically.

## Install

```
dotnet add package ZibStack.NET.Log
```

## Quick Start

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

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/log/](https://mistykuu.github.io/ZibStack.NET/packages/log/)
