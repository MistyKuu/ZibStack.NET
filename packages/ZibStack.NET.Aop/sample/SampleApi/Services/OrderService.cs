using ZibStack.NET.Aop.Aspects;

namespace ZibStack.NET.Aop.Sample.Services;

public class Order
{
    public int Id { get; set; }
    public string Product { get; set; } = "";
    public decimal Total { get; set; }
}

public class OrderService
{
    [Timing]
    public Order GetOrder(int id)
    {
        Thread.Sleep(50);
        return new Order { Id = id, Product = "Widget", Total = 29.97m };
    }

    [Timing]
    public async Task<decimal> CalculateTotalAsync(int orderId)
    {
        await Task.Delay(30);
        return orderId * 12.50m;
    }
}

[RequirePermission]
public class OrderService2
{
    public void Debug(int a)
    {
        
    }
}
