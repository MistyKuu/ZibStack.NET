# ZibStack.NET.Result

Functional `Result<T>` monad for .NET — eliminate manual error propagation with `Map`, `Bind`, and `Match`.

## Install

```
dotnet add package ZibStack.NET.Result
```

## Quick Start

```csharp
Result<OrderDto> dto = _orderService.GetOrder(id)
    .Map(order => MapToDto(order));

Result<string> tracking = GetUser(id)
    .Bind(user => GetLatestOrder(user.Id))
    .Bind(order => GetShipment(order.ShipmentId))
    .Map(shipment => shipment.TrackingNumber);

string message = result.Match(
    value => $"Found: {value}",
    error => $"Error: {error.Message}");
```

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/result/](https://mistykuu.github.io/ZibStack.NET/packages/result/)
