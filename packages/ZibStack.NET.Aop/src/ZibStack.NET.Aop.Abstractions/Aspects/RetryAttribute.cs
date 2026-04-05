namespace ZibStack.NET.Aop.Aspects;

/// <summary>
/// Retries the method on exception up to <see cref="MaxAttempts"/> times.
/// Supports constant delay and exponential backoff.
/// </summary>
/// <example>
/// <code>
/// [Retry(MaxAttempts = 3, DelayMs = 100)]
/// public Order GetOrder(int id) { ... }
///
/// // Exponential backoff: 100ms, 200ms, 400ms
/// [Retry(MaxAttempts = 4, DelayMs = 100, BackoffMultiplier = 2.0)]
/// public async Task&lt;Order&gt; GetOrderAsync(int id) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(RetryHandler))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RetryAttribute : AspectAttribute
{
    public int MaxAttempts { get; set; } = 3;
    public int DelayMs { get; set; }
    public double BackoffMultiplier { get; set; } = 1.0;
}

public sealed class RetryHandler : IAroundAspectHandler
{
    public object? Around(AspectContext context, Func<object?> proceed)
    {
        var maxAttempts = 3;
        var delayMs = 0;
        var backoff = 1.0;

        if (context.Properties.TryGetValue("MaxAttempts", out var ma) && ma is int m) maxAttempts = m;
        if (context.Properties.TryGetValue("DelayMs", out var dm) && dm is int d) delayMs = d;
        if (context.Properties.TryGetValue("BackoffMultiplier", out var bm) && bm is double b) backoff = b;

        var currentDelay = delayMs;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return proceed();
            }
            catch when (attempt < maxAttempts)
            {
                if (currentDelay > 0)
                    System.Threading.Thread.Sleep(currentDelay);
                currentDelay = (int)(currentDelay * backoff);
            }
        }
        return proceed(); // final attempt — let exception propagate
    }
}
