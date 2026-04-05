using ZibStack.NET.Log.Sample.Services;
using ZibStack.NET.Log;

// Assembly-level defaults: all [Log] methods in this project use Debug level
// and JSON mode unless overridden per-method.
[assembly: ZibLogDefaults(EntryExitLevel = ZibLogLevel.Debug, ObjectLogging = ObjectLogMode.Json)]

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<OrderService>();

var app = builder.Build();

app.MapGet("/order", (OrderService service) =>
{
    var order = service.PlaceOrder(42, "Widget", 3);
    return Results.Ok(order);
});

app.MapGet("/total/{orderId:int}", async (int orderId, OrderService service) =>
{
    var total = await service.CalculateTotalAsync(orderId);
    return Results.Ok(new { orderId, total });
});

app.MapGet("/payment/{orderId:int}", (int orderId, OrderService service) =>
{
    var order = service.GetOrderWithPayment(orderId, "4111-1111-1111-1111", new byte[] { 1, 2, 3 });
    return Results.Ok(order);
});

app.MapGet("/destructure", (OrderService service) =>
{
    var order = new Order { Id = 1, CustomerId = 42, Product = "Widget", Quantity = 5, Total = 49.95m };
    return Results.Ok(service.TestDestructure(order));
});

app.MapGet("/json", (OrderService service) =>
{
    var order = new Order { Id = 1, CustomerId = 42, Product = "Widget", Quantity = 5, Total = 49.95m };
    return Results.Ok(service.TestJson(order));
});

app.MapGet("/tostring", (OrderService service) =>
{
    var order = new Order { Id = 1, CustomerId = 42, Product = "Widget", Quantity = 5, Total = 49.95m };
    return Results.Ok(service.TestToString(order));
});

app.MapGet("/apikey/{userId:int}", (int userId, OrderService service) =>
{
    var key = service.GetApiKey(userId);
    return Results.Ok(new { key });
});

app.MapGet("/sanitized", (OrderService service) =>
{
    var order = new Order
    {
        Id = 1, CustomerId = 42, Product = "Widget", Quantity = 3, Total = 29.97m,
        CreditCard = "4111-1111-1111-1111",
        RawPayload = new byte[] { 1, 2, 3 },
        Customer = new CustomerInfo
        {
            Name = "John", Email = "john@secret.com",
            Address = new AddressInfo { City = "Warsaw", Street = "Secret St. 123" }
        }
    };
    var result = service.TestJson(order);
    return Results.Ok(result);
});

app.MapGet("/interpolated", (OrderService service, ILogger<Program> logger) =>
{
    var userId = 42;
    var product = "Widget";
    var total = 29.97m;

    // ZibStack interpolated string logging — structured + natural syntax
    logger.LogInformationEx($"User {userId} bought {product} for {total:C}");
    logger.LogDebugEx($"Processing order for user {userId}");
    logger.LogWarningEx($"Low stock for {product}");

    return Results.Ok(new { userId, product, total });
});

app.MapGet("/ping", (OrderService service) =>
{
    service.Ping();
    return Results.Ok("pong");
});

app.Run();
