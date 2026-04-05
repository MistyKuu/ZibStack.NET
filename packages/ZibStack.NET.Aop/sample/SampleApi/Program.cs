using ZibStack.NET.Aop.Sample.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<OrderService>();
var app = builder.Build();

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

app.Run();
