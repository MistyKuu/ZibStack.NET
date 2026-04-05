using ZibStack.NET.Aop;

namespace ZibStack.NET.Aop.Sample.Aspects;

[AspectHandler(typeof(RetryHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
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
        var currentDelay = 0;
        var backoff = 1.0;

        if (context.Properties.TryGetValue("MaxAttempts", out var ma) && ma is int m) maxAttempts = m;
        if (context.Properties.TryGetValue("DelayMs", out var dm) && dm is int d) currentDelay = d;
        if (context.Properties.TryGetValue("BackoffMultiplier", out var bm) && bm is double b) backoff = b;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try { return proceed(); }
            catch when (attempt < maxAttempts)
            {
                if (currentDelay > 0) Thread.Sleep(currentDelay);
                currentDelay = (int)(currentDelay * backoff);
            }
        }
        return proceed();
    }
}
