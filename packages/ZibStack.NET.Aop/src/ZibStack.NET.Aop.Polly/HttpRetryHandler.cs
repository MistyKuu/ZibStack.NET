using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.Retry;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in handler for <see cref="HttpRetryAttribute"/>. Uses Polly to retry on transient
/// HTTP errors — same logic as <c>Microsoft.Extensions.Http.Resilience</c>.
///
/// <para>
/// Handles:
/// <list type="bullet">
///   <item><see cref="HttpRequestException"/> with transient status codes (408, 429, 5xx)</item>
///   <item><see cref="HttpRequestException"/> without a status code (network-level failure)</item>
///   <item><see cref="TaskCanceledException"/> (HTTP client timeout)</item>
/// </list>
/// </para>
/// </summary>
public sealed class PollyHttpRetryHandler : IAroundAspectHandler, IAsyncAroundAspectHandler
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
        var maxRetry = 3;
        var delayMs = 200;

        if (context.Properties.TryGetValue("MaxRetryAttempts", out var mr) && mr is int m) maxRetry = m;
        if (context.Properties.TryGetValue("DelayMs", out var dm) && dm is int d) delayMs = d;

        var cacheKey = $"http:{maxRetry}:{delayMs}";

        return PipelineCache.GetOrAdd(cacheKey, _ =>
            new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = maxRetry,
                    Delay = TimeSpan.FromMilliseconds(delayMs),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = args => new ValueTask<bool>(IsTransientHttpError(args.Outcome.Exception)),
                })
                .Build());
    }

    private static bool IsTransientHttpError(Exception? exception) => exception switch
    {
        HttpRequestException { StatusCode: { } status } => IsTransientStatusCode(status),
        HttpRequestException => true,                          // no status code = network failure
        TaskCanceledException => true,                         // HTTP client timeout
        _ => false,
    };

    private static bool IsTransientStatusCode(HttpStatusCode status) => status is
        HttpStatusCode.RequestTimeout or          // 408
        HttpStatusCode.TooManyRequests or          // 429
        HttpStatusCode.InternalServerError or      // 500
        HttpStatusCode.BadGateway or               // 502
        HttpStatusCode.ServiceUnavailable or       // 503
        HttpStatusCode.GatewayTimeout;             // 504
}
