using ZibStack.NET.Log;
using Microsoft.Extensions.Logging;

namespace ZibStack.NET.Log.Sample.Services;

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Product { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Total { get; set; }
    [Sensitive] public string? CreditCard { get; set; }
    [NoLog] public byte[]? RawPayload { get; set; }
    public CustomerInfo? Customer { get; set; }
}

public class CustomerInfo
{
    public string Name { get; set; } = "";
    [Sensitive] public string Email { get; set; } = "";
    public AddressInfo? Address { get; set; }
}

public class AddressInfo
{
    public string City { get; set; } = "";
    [Sensitive] public string Street { get; set; } = "";
}

public class OrderService
{
    private readonly ILogger<OrderService> _logger;

    public OrderService(ILogger<OrderService> logger)
    {
        _logger = logger;
        _logger.LogInformation("Starting Order Service {asd}", "asd");

        // Structured logging with standard LogXxx — just add: using ZibStack.NET.Log;
        // C# automatically picks the handler overload for $"..." arguments.
        var version = "1.0";
        _logger.LogInformation($"Order Service v{version} initialized");
        // ↑ template: "Order Service v{version} initialized", args: ["1.0"]
    }

    [Log]
    public Order PlaceOrder(int customerId, string product, int quantity)
    {
        // Simulate some work
        Thread.Sleep(50);
        return new Order
        {
            Id = Random.Shared.Next(1000),
            CustomerId = customerId,
            Product = product,
            Quantity = quantity,
            Total = quantity * 9.99m
        };
    }

    [Log(EntryExitLevel = ZibLogLevel.Debug, MeasureElapsed = true)]
    public async Task<decimal> CalculateTotalAsync(int orderId)
    {
        await Task.Delay(30);
        return orderId * 12.50m;
    }

    [Log]
    public Order GetOrderWithPayment(int orderId, [Sensitive] string creditCardNumber, [NoLog] byte[] rawPayload)
    {
        return new Order { Id = orderId, Total = 99.99m };
    }
    
    
    // Default: Destructure — {@param} for structured logging
    [Log]
    public Order TestDestructure(Order order)
    {
        return order;
    }

    // JSON mode — full serialization
    [Log(ObjectLogging = ObjectLogMode.Json)]
    public Order TestJson(Order order)
    {
        return order;
    }

    // ToString mode — object.ToString()
    [Log(ObjectLogging = ObjectLogMode.ToString)]
    public Order TestToString(Order order)
    {
        return order;
    }

    [Log]
    [return: Sensitive]
    public string GetApiKey(int userId)
    {
        return "sk-secret-key-12345";
    }

    [Log(LogParameters = false, LogReturnValue = false, MeasureElapsed = false)]
    public void Ping()
    {
        // Health check - minimal logging
    }
}
