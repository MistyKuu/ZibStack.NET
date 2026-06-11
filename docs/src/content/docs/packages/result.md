---
title: ZibStack.NET.Result
description: Functional Result monad for .NET — eliminate manual error propagation across layers with Map, Bind, Match, and async pipelines.
---

[![NuGet](https://img.shields.io/nuget/v/ZibStack.NET.Result.svg)](https://www.nuget.org/packages/ZibStack.NET.Result) [![Source](https://img.shields.io/badge/source-GitHub-blue)](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Result)

Functional `Result<T>` monad for .NET — eliminate manual error propagation across layers.

## Install

```bash
dotnet add package ZibStack.NET.Result
```

## The Problem

```csharp
// Every layer re-checks and re-wraps errors manually:
Result<Order> orderResult = _orderService.GetOrder(id);
if (orderResult.IsFailure)
    return Result<OrderDto>.Failure(orderResult.Error); // maintenance hell

var dto = MapToDto(orderResult.Value);
return Result<OrderDto>.Success(dto);
```

## The Solution

```csharp
// Map — transform the value, errors propagate automatically
Result<OrderDto> dto = _orderService.GetOrder(id)
    .Map(order => MapToDto(order));

// Bind — chain Result-returning calls
Result<ShipmentDto> result = _orderService.GetOrder(id)
    .Bind(order => _shipmentService.GetShipment(order.ShipmentId))
    .Map(shipment => MapToDto(shipment));
```

## Quick Start

### Creating Results

```csharp
// Success
Result<int> ok = Result<int>.Success(42);

// Failure
Result<int> fail = Result<int>.Failure(Error.NotFound("Order not found"));

// Implicit conversions
Result<int> ok2 = 42;
Result<int> fail2 = Error.Validation("Invalid input");

// From nullable
Order? order = db.Find(id);
Result<Order> result = order.ToResult(Error.NotFound($"Order {id} not found"));
```

### Built-in Error Types

```csharp
Error.Validation("Name is required")
Error.NotFound("User not found")
Error.Conflict("Email already exists")
Error.Unauthorized("Invalid token")
Error.Forbidden("Insufficient permissions")
Error.Unexpected("Something went wrong")

// Aggregate errors
Error.Validation("Multiple errors", new[] { error1, error2 })
```

### Map & Bind

```csharp
// Map — transform value (T → K)
Result<string> name = GetUser(id)
    .Map(user => user.Name);

// Bind — chain operations (T → Result<K>)
Result<Order> order = GetUser(id)
    .Bind(user => GetLatestOrder(user.Id));

// Chain multiple
Result<string> tracking = GetUser(id)
    .Bind(user => GetLatestOrder(user.Id))
    .Bind(order => GetShipment(order.ShipmentId))
    .Map(shipment => shipment.TrackingNumber);
```

### Match & Switch

```csharp
// Match — extract a value from either path
string message = result.Match(
    value => $"Found: {value}",
    error => $"Error: {error.Message}");

// Switch — execute side effects
result.Switch(
    value => Console.WriteLine($"OK: {value}"),
    error => logger.LogError(error.Message));
```

### Ensure

```csharp
Result<int> result = GetAge()
    .Ensure(age => age >= 18, Error.Validation("Must be 18+"))
    .Ensure(age => age <= 120, Error.Validation("Invalid age"));
```

### Tap

```csharp
// Execute a side effect without changing the result
var result = GetOrder(id)
    .Tap(order => logger.LogInformation("Found order {Id}", order.Id))
    .Map(order => MapToDto(order));
```

### Fallbacks

```csharp
Result<Config> config = LoadFromFile()
    .OrElse(_ => LoadFromEnvironment())
    .OrElse(_ => Result<Config>.Success(Config.Default));
```

## Async Support

Full async pipeline support — chain `Task<Result<T>>` without awaiting each step:

```csharp
Result<ShipmentDto> result = await GetOrderAsync(id)
    .BindAsync(order => GetShipmentAsync(order.ShipmentId))
    .MapAsync(shipment => MapToDto(shipment));

// Async mapper/binder
Result<byte[]> data = await GetUrlAsync(id)
    .BindAsync(async url =>
    {
        var response = await httpClient.GetAsync(url);
        return response.IsSuccessStatusCode
            ? Result<byte[]>.Success(await response.Content.ReadAsByteArrayAsync())
            : Result<byte[]>.Failure(Error.Unexpected("Download failed"));
    });

// TapAsync with async side effect
var result = await GetOrderAsync(id)
    .TapAsync(async order => await PublishEventAsync(order))
    .MapAsync(order => MapToDto(order));
```

Supported async extensions: `MapAsync`, `BindAsync`, `MatchAsync`, `TapAsync`, `SwitchAsync`, `GetValueOrDefaultAsync`, `OrElseAsync`.

Both `Task<Result<T>>` and `ValueTask<Result<T>>` overloads are provided.

## Combining Results

```csharp
// Combine — fail on first error
var results = new[] { Validate(a), Validate(b), Validate(c) };
Result<IReadOnlyList<int>> combined = results.Combine();

// CombineAll — collect ALL errors
Result<IReadOnlyList<int>> all = results.CombineAll();
// all.Error.InnerErrors contains every individual error
```

## Real-World Example

```csharp
public class OrderService
{
    public async Task<Result<OrderConfirmation>> PlaceOrderAsync(CreateOrderRequest request)
    {
        return await ValidateRequest(request)
            .BindAsync(req => FindCustomerAsync(req.CustomerId))
            .BindAsync(customer => CreateOrderAsync(customer, request))
            .TapAsync(order => SendConfirmationEmailAsync(order))
            .MapAsync(order => new OrderConfirmation(order.Id, order.Total));
    }

    private Result<CreateOrderRequest> ValidateRequest(CreateOrderRequest request)
    {
        return Result<CreateOrderRequest>.Success(request)
            .Ensure(r => r.Items.Count > 0, Error.Validation("Order must have items"))
            .Ensure(r => r.CustomerId > 0, Error.Validation("Invalid customer"));
    }
}
```

## ASP.NET Integration

`Result<T>` integrates naturally with ASP.NET minimal APIs and controllers. The library doesn't ship a built-in ASP.NET adapter (to avoid a dependency on `Microsoft.AspNetCore`), but a small extension method bridges the gap:

### Extension method pattern

```csharp
using Microsoft.AspNetCore.Http;

public static class ResultExtensions
{
    public static IResult ToResponse<T>(this Result<T> result) =>
        result.Match(
            value => Results.Ok(value),
            error => error.Code switch
            {
                "NotFound"     => Results.NotFound(error.Message),
                "Validation"   => Results.BadRequest(error.Message),
                "Unauthorized" => Results.Unauthorized(),
                "Forbidden"    => Results.Forbid(),
                "Conflict"     => Results.Conflict(error.Message),
                _              => Results.Problem(error.Message)
            });
}
```

### Usage in minimal APIs

```csharp
app.MapGet("/api/orders/{id}", (int id, OrderService svc) =>
    svc.GetOrder(id).ToResponse());

app.MapPost("/api/orders", async (CreateOrderRequest req, OrderService svc) =>
    (await svc.PlaceOrderAsync(req)).ToResponse());
```

### Usage in controllers

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly OrderService _svc;

    [HttpGet("{id}")]
    public IActionResult Get(int id) =>
        _svc.GetOrder(id).Match<IActionResult>(
            value => Ok(value),
            error => error.Code switch
            {
                "NotFound" => NotFound(error.Message),
                "Validation" => BadRequest(error.Message),
                _ => StatusCode(500, error.Message)
            });
}
```

### Async pipeline example

```csharp
app.MapPost("/api/orders/{id}/ship", async (int id, ShipmentService svc) =>
    (await svc.GetOrder(id)
        .BindAsync(order => svc.CreateShipment(order))
        .TapAsync(shipment => svc.NotifyCustomer(shipment))
        .MapAsync(shipment => new { shipment.TrackingNumber }))
    .ToResponse());
```

> **Tip:** Place the `ToResponse()` extension in a shared project or `ServiceCollectionExtensions` file so all endpoints can use it consistently.

## AOP integration

When a method returns `Result` / `Result<T>`, ZibStack.NET.Aop aspects cooperate with the Result pattern: `[Authorize]` failures come back as `Error.Unauthorized` and `[Validate]` failures as `Error.Validation` — no exceptions, no try/catch:

```csharp
[Authorize(Roles = "Admin")]
public async Task<Result<Order>> DeleteOrderAsync(int id) { ... }

var result = await svc.DeleteOrderAsync(7);   // never throws on authorization failure
if (result.IsFailure) return result.ToResponse();
```

See [Aop → Built-in aspects](/packages/aop/built-in/) for details.

## Requirements

- .NET 8.0+ (async extensions)
- .NET Standard 2.0 (core Result/Error types)

## License

MIT
