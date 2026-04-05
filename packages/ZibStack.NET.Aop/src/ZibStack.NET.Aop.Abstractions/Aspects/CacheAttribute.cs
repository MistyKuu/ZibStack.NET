using System.Collections.Concurrent;

namespace ZibStack.NET.Aop.Aspects;

/// <summary>
/// Caches method return value based on parameter values.
/// With DI, register an <see cref="IAspectCache"/> implementation (e.g. wrapping IMemoryCache/IDistributedCache).
/// Without DI, uses a built-in in-memory <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
/// <example>
/// <code>
/// // Option 1 — DI with custom cache:
/// builder.Services.AddSingleton&lt;IAspectCache, RedisAspectCache&gt;();
/// builder.Services.AddTransient&lt;CacheHandler&gt;();
///
/// // Option 2 — built-in in-memory (no DI needed):
/// [Cache(DurationSeconds = 60)]
/// public Order GetOrder(int id) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(CacheHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class CacheAttribute : AspectAttribute
{
    /// <summary>Cache duration in seconds. Default: 300 (5 minutes). 0 = no expiration.</summary>
    public int DurationSeconds { get; set; } = 300;
}

/// <summary>
/// Cache abstraction for <see cref="CacheHandler"/>. Register in DI to provide
/// custom caching (Redis, IMemoryCache, IDistributedCache, etc.).
/// </summary>
public interface IAspectCache
{
    bool TryGet(string key, out object? value);
    void Set(string key, object? value, int durationSeconds);
    void Remove(string key);
}

public sealed class CacheHandler : IAroundAspectHandler
{
    private static readonly ConcurrentDictionary<string, (object? Value, DateTime Expiry)> _fallbackCache = new();
    private readonly IAspectCache? _cache;

    public CacheHandler() { }

    public CacheHandler(IAspectCache cache) => _cache = cache;

    public object? Around(AspectContext context, Func<object?> proceed)
    {
        var key = $"{context.ClassName}.{context.MethodName}({context.FormatParameters()})";
        var durationSeconds = 300;
        if (context.Properties.TryGetValue("DurationSeconds", out var ds) && ds is int d)
            durationSeconds = d;

        if (_cache != null)
            return AroundWithDI(key, durationSeconds, proceed);

        return AroundWithFallback(key, durationSeconds, proceed);
    }

    private object? AroundWithDI(string key, int durationSeconds, Func<object?> proceed)
    {
        if (_cache!.TryGet(key, out var cached))
            return cached;

        var result = proceed();
        _cache.Set(key, result, durationSeconds);
        return result;
    }

    private static object? AroundWithFallback(string key, int durationSeconds, Func<object?> proceed)
    {
        if (_fallbackCache.TryGetValue(key, out var entry))
        {
            if (entry.Expiry == DateTime.MaxValue || entry.Expiry > DateTime.UtcNow)
                return entry.Value;
            _fallbackCache.TryRemove(key, out _);
        }

        var result = proceed();

        var expiry = durationSeconds > 0
            ? DateTime.UtcNow.AddSeconds(durationSeconds)
            : DateTime.MaxValue;

        _fallbackCache[key] = (result, expiry);
        return result;
    }

    /// <summary>Clears the built-in fallback cache.</summary>
    public static void ClearAll() => _fallbackCache.Clear();

    /// <summary>Removes entries matching a method name from the fallback cache.</summary>
    public static void Invalidate(string methodName)
    {
        foreach (var key in _fallbackCache.Keys)
            if (key.Contains(methodName))
                _fallbackCache.TryRemove(key, out _);
    }
}
