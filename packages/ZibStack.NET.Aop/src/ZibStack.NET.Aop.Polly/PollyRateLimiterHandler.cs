using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Polly;
using Polly.RateLimiting;

namespace ZibStack.NET.Aop;

/// <summary>
/// Handler for <see cref="PollyRateLimiterAttribute"/>. Creates a fixed window rate limiter
/// per method and caches it.
/// </summary>
public sealed class PollyRateLimiterHandler : IAroundAspectHandler, IAsyncAroundAspectHandler
{
    private static readonly ConcurrentDictionary<string, ResiliencePipeline> PipelineCache = new();

    /// <inheritdoc />
    public object? Around(AspectContext context, Func<object?> proceed)
    {
        var pipeline = GetPipeline(context);
        return pipeline.Execute(_ => proceed(), CancellationToken.None);
    }

    /// <inheritdoc />
    public async ValueTask<object?> AroundAsync(AspectContext context, Func<ValueTask<object?>> proceed)
    {
        var pipeline = GetPipeline(context);
        return await pipeline.ExecuteAsync(
            async ct => await proceed().ConfigureAwait(false),
            CancellationToken.None).ConfigureAwait(false);
    }

    private static ResiliencePipeline GetPipeline(AspectContext context)
    {
        var permitLimit = 100;
        var windowSeconds = 60;
        var queueLimit = 0;

        if (context.Properties.TryGetValue("PermitLimit", out var pl) && pl is int p) permitLimit = p;
        if (context.Properties.TryGetValue("WindowSeconds", out var ws) && ws is int w) windowSeconds = w;
        if (context.Properties.TryGetValue("QueueLimit", out var ql) && ql is int q) queueLimit = q;

        var cacheKey = $"rl:{context.ClassName}.{context.MethodName}:{permitLimit}:{windowSeconds}:{queueLimit}";

        return PipelineCache.GetOrAdd(cacheKey, _ =>
            new ResiliencePipelineBuilder()
                .AddRateLimiter(new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = queueLimit,
                }))
                .Build());
    }
}
