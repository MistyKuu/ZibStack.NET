using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly.Registry;

namespace ZibStack.NET.Aop;

/// <summary>
/// DI registration for Polly-based aspect handlers.
/// </summary>
public static class AopPollyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PollyRetryHandler"/> and <see cref="PollyHttpRetryHandler"/> in DI.
    /// Call after <c>AddAop()</c>.
    ///
    /// <para>
    /// If <see cref="ResiliencePipelineProvider{TKey}"/> is available in DI
    /// (e.g. via <c>AddResiliencePipeline</c>), <see cref="PollyRetryHandler"/> uses it
    /// for named pipeline resolution. Otherwise only inline pipelines are supported.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAopPolly(this IServiceCollection services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton<PollyRetryHandler>(sp =>
        {
            var provider = sp.GetService<ResiliencePipelineProvider<string>>();
            return provider is not null ? new PollyRetryHandler(provider) : new PollyRetryHandler();
        });

        services.TryAddSingleton<PollyHttpRetryHandler>();
        services.TryAddSingleton<PollyCircuitBreakerHandler>();
        services.TryAddSingleton<PollyRateLimiterHandler>();

        return services;
    }
}
