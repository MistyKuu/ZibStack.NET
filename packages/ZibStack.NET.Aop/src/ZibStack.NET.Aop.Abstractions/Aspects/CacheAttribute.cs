using System.Collections.Concurrent;

namespace ZibStack.NET.Aop.Aspects;

/// <summary>
/// Caches method return value based on parameter values.
/// Uses an in-memory <see cref="ConcurrentDictionary{TKey,TValue}"/> cache.
/// </summary>
/// <example>
/// <code>
/// [Cache(DurationSeconds = 60)]
/// public Order GetOrder(int id) { ... }
/// // First call: executes method, caches result
/// // Subsequent calls with same id: returns cached value
/// </code>
/// </example>
[AspectHandler(typeof(CacheHandler))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CacheAttribute : AspectAttribute
{
    /// <summary>Cache duration in seconds. Default: 300 (5 minutes). 0 = no expiration.</summary>
    public int DurationSeconds { get; set; } = 300;
}

public sealed class CacheHandler : IAroundAspectHandler
{
    private static readonly ConcurrentDictionary<string, (object? Value, DateTime Expiry)> _cache = new();

    public object? Around(AspectContext context, Func<object?> proceed)
    {
        var key = $"{context.ClassName}.{context.MethodName}({context.FormatParameters()})";

        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.Expiry == DateTime.MaxValue || entry.Expiry > DateTime.UtcNow)
                return entry.Value;
            _cache.TryRemove(key, out _);
        }

        var result = proceed();

        var durationSeconds = 300;
        if (context.Properties.TryGetValue("DurationSeconds", out var ds) && ds is int d)
            durationSeconds = d;

        var expiry = durationSeconds > 0
            ? DateTime.UtcNow.AddSeconds(durationSeconds)
            : DateTime.MaxValue;

        _cache[key] = (result, expiry);
        return result;
    }

    /// <summary>Clears the entire cache.</summary>
    public static void ClearAll() => _cache.Clear();

    /// <summary>Removes a specific cache entry by key pattern.</summary>
    public static void Invalidate(string methodName)
    {
        foreach (var key in _cache.Keys)
            if (key.Contains(methodName))
                _cache.TryRemove(key, out _);
    }
}
