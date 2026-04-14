using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.Registry;
using Polly.Retry;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in handler for <see cref="PollyRetryAttribute"/>. Uses Polly's <see cref="ResiliencePipeline"/>
/// for retry logic with exponential/linear/constant backoff and optional named pipeline references.
///
/// <para>
/// Implements both <see cref="IAroundAspectHandler"/> and <see cref="IAsyncAroundAspectHandler"/>
/// so it works on sync and async methods.
/// </para>
///
/// <para>
/// When <see cref="PollyRetryAttribute.PipelineName"/> is set, the handler resolves the pipeline
/// from <see cref="ResiliencePipelineProvider{TKey}"/> in DI (requires <c>Microsoft.Extensions.Resilience</c>).
/// Otherwise, it builds an inline pipeline from the attribute properties and caches it.
/// </para>
/// </summary>
public sealed class PollyRetryHandler : IAroundAspectHandler, IAsyncAroundAspectHandler
{
    private readonly ResiliencePipelineProvider<string>? _pipelineProvider;
    private static readonly ConcurrentDictionary<string, ResiliencePipeline> PipelineCache = new();

    /// <summary>
    /// Creates a handler with a pipeline provider for named pipeline resolution.
    /// </summary>
    public PollyRetryHandler(ResiliencePipelineProvider<string> pipelineProvider) =>
        _pipelineProvider = pipelineProvider;

    /// <summary>
    /// Creates a handler without a pipeline provider (inline pipelines only).
    /// </summary>
    public PollyRetryHandler() => _pipelineProvider = null;

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
        return await pipeline.ExecuteAsync(async ct => await proceed().ConfigureAwait(false),
            CancellationToken.None).ConfigureAwait(false);
    }

    private ResiliencePipeline GetPipeline(AspectContext context)
    {
        // Named pipeline — resolve from DI
        if (context.Properties.TryGetValue("PipelineName", out var pn) && pn is string name && name.Length > 0)
        {
            if (_pipelineProvider is null)
                throw new InvalidOperationException(
                    $"PollyRetryAttribute.PipelineName '{name}' requires ResiliencePipelineProvider<string> in DI. " +
                    $"Add 'builder.Services.AddResiliencePipeline(\"{name}\", ...)' at startup.");

            return _pipelineProvider.GetPipeline(name);
        }

        // Inline pipeline — build from attribute properties and cache
        var maxRetry = 3;
        var delayMs = 200;
        var backoffType = RetryBackoffType.Exponential;
        Type[]? handleTypes = null;
        Type[]? ignoreTypes = null;

        if (context.Properties.TryGetValue("MaxRetryAttempts", out var mr) && mr is int m) maxRetry = m;
        if (context.Properties.TryGetValue("DelayMs", out var dm) && dm is int d) delayMs = d;
        if (context.Properties.TryGetValue("BackoffType", out var bt) && bt is int b) backoffType = (RetryBackoffType)b;
        if (context.Properties.TryGetValue("Handle", out var hf) && hf is Type[] h) handleTypes = h;
        if (context.Properties.TryGetValue("Ignore", out var ig) && ig is Type[] i) ignoreTypes = i;

        var handleKey = handleTypes is not null ? string.Join(",", handleTypes.Select(t => t.FullName)) : "";
        var ignoreKey = ignoreTypes is not null ? string.Join(",", ignoreTypes.Select(t => t.FullName)) : "";
        var cacheKey = $"{maxRetry}:{delayMs}:{(int)backoffType}:{handleKey}:{ignoreKey}";

        return PipelineCache.GetOrAdd(cacheKey, _ =>
        {
            var pollyBackoff = backoffType switch
            {
                RetryBackoffType.Constant => DelayBackoffType.Constant,
                RetryBackoffType.Linear => DelayBackoffType.Linear,
                RetryBackoffType.Exponential => DelayBackoffType.Exponential,
                _ => DelayBackoffType.Exponential,
            };

            var options = new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetry,
                Delay = TimeSpan.FromMilliseconds(delayMs),
                BackoffType = pollyBackoff,
                UseJitter = backoffType == RetryBackoffType.Exponential,
            };

            // Exception filtering
            if (handleTypes is not null)
            {
                var captured = handleTypes;
                options.ShouldHandle = args => new ValueTask<bool>(
                    args.Outcome.Exception is not null && MatchesAny(args.Outcome.Exception, captured));
            }
            else if (ignoreTypes is not null)
            {
                var captured = ignoreTypes;
                options.ShouldHandle = args => new ValueTask<bool>(
                    args.Outcome.Exception is not null && !MatchesAny(args.Outcome.Exception, captured));
            }

            return new ResiliencePipelineBuilder()
                .AddRetry(options)
                .Build();
        });
    }

    private static bool MatchesAny(Exception ex, Type[] types)
    {
        var exType = ex.GetType();
        foreach (var t in types)
        {
            if (t.IsAssignableFrom(exType))
                return true;
        }
        return false;
    }
}
