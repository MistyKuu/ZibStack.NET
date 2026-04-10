using ZibStack.NET.Aop;
// using ZibStack.NET.Log; ← no longer needed! Generator emits global using automatically.
using ZibStack.NET.Log.Sample.Services;

[assembly: ZibLogDefaults(EntryExitLevel = ZibLogLevel.Debug, ObjectLogging = ObjectLogMode.Json)]

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<OrderService>();

var app = builder.Build();

AspectServiceProvider.ServiceProvider = app.Services;

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

    // Structured interpolated string logging — just works, no Ex suffix needed
    logger.LogInformation($"User {userId} bought {product} for {total:C}");
    logger.LogDebug($"Processing order for user {userId}");
    logger.LogWarning($"Low stock for {product}");

    return Results.Ok(new { userId, product, total });
});

app.MapGet("/structured", (ILogger<Program> logger) =>
{
    var userId = 42;
    var product = "Widget";
    var total = 29.97m;

    // Capture structured log entries to prove template is preserved
    var captured = new List<object>();

    // NEW: Standard LogXxx with $"..." — structured logging automatically!
    logger.LogInformation($"User {userId} bought {product} for {total:C}");
    logger.LogWarning($"Low stock for {product}, only {3} left");

    // Non-interpolated still uses Microsoft's methods:
    logger.LogInformation("Processing complete — no interpolation here");
    logger.LogInformation("Template with args: User {Name}", "Alice");

    return Results.Ok(new { message = "Check console logs for structured output" });
});

// Endpoint that captures log entries and returns structured info (template + args)
app.MapGet("/structured-proof", () =>
{
    var entries = new List<object>();
    var testLogger = new StructuredTestLogger(entries);

    var userId = 42;
    var product = "Widget";
    var total = 29.97m;

    // With ZibStack.NET.Log handler (structured):
    testLogger.LogInformation($"User {userId} bought {product} for {total:C}");
    testLogger.LogWarning($"Low stock for {product}, only {3} left");

    return Results.Ok(new { structured_log_entries = entries });
});

app.MapGet("/ping", (OrderService service) =>
{
    service.Ping();
    return Results.Ok("pong");
});

app.Run();

sealed class StructuredTestLogger : ILogger
{
    private readonly List<object> _entries;
    public StructuredTestLogger(List<object> entries) => _entries = entries;
    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        string? template = null;
        var properties = new Dictionary<string, object?>();

        if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
        {
            foreach (var kvp in values)
            {
                if (kvp.Key == "{OriginalFormat}")
                    template = kvp.Value?.ToString();
                else
                    properties[kvp.Key] = kvp.Value;
            }
        }

        _entries.Add(new
        {
            level = logLevel.ToString(),
            template,
            properties,
            formatted = formatter(state, exception)
        });
    }
}
