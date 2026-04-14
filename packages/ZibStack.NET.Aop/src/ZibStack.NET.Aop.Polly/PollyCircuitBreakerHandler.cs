using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.CircuitBreaker;

namespace ZibStack.NET.Aop;

/// <summary>
/// Handler for <see cref="PollyCircuitBreakerAttribute"/>. Creates one circuit breaker pipeline
/// per unique configuration and caches it.
/// </summary>
public sealed class PollyCircuitBreakerHandler : IAroundAspectHandler, IAsyncAroundAspectHandler
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
        var failureThreshold = 0.5;
        var minimumThroughput = 10;
        var samplingDuration = 30;
        var breakDuration = 15;

        if (context.Properties.TryGetValue("FailureThreshold", out var ft) && ft is double f) failureThreshold = f;
        if (context.Properties.TryGetValue("MinimumThroughput", out var mt) && mt is int m) minimumThroughput = m;
        if (context.Properties.TryGetValue("SamplingDurationSeconds", out var sd) && sd is int s) samplingDuration = s;
        if (context.Properties.TryGetValue("BreakDurationSeconds", out var bd) && bd is int b) breakDuration = b;

        // Key includes class+method so each decorated method gets its own circuit
        var cacheKey = $"cb:{context.ClassName}.{context.MethodName}:{failureThreshold}:{minimumThroughput}:{samplingDuration}:{breakDuration}";

        return PipelineCache.GetOrAdd(cacheKey, _ =>
            new ResiliencePipelineBuilder()
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = failureThreshold,
                    MinimumThroughput = minimumThroughput,
                    SamplingDuration = TimeSpan.FromSeconds(samplingDuration),
                    BreakDuration = TimeSpan.FromSeconds(breakDuration),
                })
                .Build());
    }
}
