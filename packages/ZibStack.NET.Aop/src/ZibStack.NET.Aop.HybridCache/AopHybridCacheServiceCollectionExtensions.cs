using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ZibStack.NET.Aop;

/// <summary>
/// DI registration for the HybridCache aspect handler.
/// </summary>
public static class AopHybridCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="HybridCacheHandler{T}"/> as an open generic in DI.
    /// Call after <c>AddAop()</c> and <c>AddHybridCache()</c>.
    /// </summary>
    public static IServiceCollection AddAopHybridCache(this IServiceCollection services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.TryAddSingleton(typeof(HybridCacheHandler<>));

        return services;
    }
}
