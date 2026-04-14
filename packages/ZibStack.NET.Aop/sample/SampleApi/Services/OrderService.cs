using ZibStack.NET.Aop;
using ZibStack.NET.Aop.Sample.Aspects;

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

[Trace]
[Metrics]
[Retry(MaxAttempts = 3, DelayMs = 100)]
public class OrderService2
{
    public void Debug(int a)
    {
    }

    [Cache(DurationSeconds = 60)]
    public string GetCachedData(int id) => $"data-{id}-{DateTime.UtcNow.Ticks}";

    [Timeout(TimeoutMs = 5000)]
    public async Task<string> SlowOperationAsync() { await Task.Delay(100); return "done"; }
}
