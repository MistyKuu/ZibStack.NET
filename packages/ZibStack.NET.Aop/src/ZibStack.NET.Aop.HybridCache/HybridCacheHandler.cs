using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Hybrid;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in handler for <see cref="HybridCacheAttribute"/>. Caches method return values using
/// <see cref="Microsoft.Extensions.Caching.Hybrid.HybridCache"/> (L1 memory + optional L2 distributed cache).
///
/// <para>
/// This is an open generic handler — the source generator closes <c>T</c> with the method's
/// return type at compile time (e.g. <c>HybridCacheHandler&lt;Product&gt;</c> for a method returning
/// <c>Task&lt;Product&gt;</c>). This ensures <see cref="HybridCache"/> serializes and deserializes
/// the correct type.
/// </para>
///
/// <para>
/// Requires <c>Microsoft.Extensions.Caching.Hybrid</c> in DI. Works on async methods only
/// (<see cref="IAsyncAroundAspectHandler{T}"/>). For sync in-memory caching, use <see cref="CacheHandler"/>.
/// </para>
/// </summary>
public sealed class HybridCacheHandler<T> : IAsyncAroundAspectHandler<T>
{
    private readonly HybridCache _cache;

    public HybridCacheHandler(HybridCache cache) =>
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    /// <inheritdoc />
    public async ValueTask<T?> AroundAsync(AspectContext context, Func<ValueTask<T?>> proceed)
    {
        var duration = 300;
        if (context.Properties.TryGetValue("DurationSeconds", out var ds) && ds is int d) duration = d;

        var key = context.Properties.TryGetValue("__cacheKey", out var ck) && ck is string k
            ? k
            : $"{context.ClassName}.{context.MethodName}({context.FormatParameters()})";

        var options = new HybridCacheEntryOptions
        {
            Expiration = duration > 0 ? TimeSpan.FromSeconds(duration) : null,
        };

        return await _cache.GetOrCreateAsync(
            key,
            async _ => (await proceed().ConfigureAwait(false))!,
            options).ConfigureAwait(false);
    }
}
