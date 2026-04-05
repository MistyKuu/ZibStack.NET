using ZibStack.NET.Aop;
using ZibStack.NET.Aop.Aspects;
using ZibStack.NET.Aop.Sample.Aspects;
using ZibStack.NET.Aop.Sample.Services;

var builder = WebApplication.CreateBuilder(args);

// Register services
builder.Services.AddSingleton<OrderService>();
builder.Services.AddSingleton<OrderService2>();

// Register aspect handlers with DI — handlers now get constructor-injected dependencies
builder.Services.AddSingleton<ITimingRecorder, LoggingTimingRecorder>();
builder.Services.AddTransient<TimingHandler>();

var app = builder.Build();

// Wire up DI for all aspect handlers
AspectServiceProvider.ServiceProvider = app.Services;

app.MapGet("/order/{id:int}", (int id, OrderService service) =>
{
    var order = service.GetOrder(id);
    return Results.Ok(order);
});

app.MapGet("/total/{id:int}", async (int id, OrderService service) =>
{
    var total = await service.CalculateTotalAsync(id);
    return Results.Ok(new { id, total });
});

app.MapGet("/debug", (OrderService2 service) =>
{
    service.Debug(123);
    return Results.Ok("ok");
});

app.Run();
