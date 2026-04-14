using System;
using System.Collections.Concurrent;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in handler for <see cref="CacheAttribute"/>. Caches method return values in a static
/// in-memory dictionary keyed by class name, method name, and formatted parameters.
///
/// <para>
/// Registered automatically by <c>AddAop()</c> as a singleton.
/// </para>
/// </summary>
public sealed class CacheHandler : IAroundAspectHandler
{
    private static readonly ConcurrentDictionary<string, (object? Value, DateTime Expiry)> Cache = new();

    /// <inheritdoc />
    public object? Around(AspectContext context, Func<object?> proceed)
    {
        // Use compile-time expanded KeyTemplate if available, otherwise fall back to default key.
        var key = context.Properties.TryGetValue("__cacheKey", out var ck) && ck is string k
            ? k
            : $"{context.ClassName}.{context.MethodName}({context.FormatParameters()})";
        var duration = 300;
        if (context.Properties.TryGetValue("DurationSeconds", out var ds) && ds is int d) duration = d;

        if (Cache.TryGetValue(key, out var entry) && (entry.Expiry == DateTime.MaxValue || entry.Expiry > DateTime.UtcNow))
            return entry.Value;

        var result = proceed();
        var expiry = duration > 0 ? DateTime.UtcNow.AddSeconds(duration) : DateTime.MaxValue;
        Cache[key] = (result, expiry);
        return result;
    }

    /// <summary>
    /// Removes all entries from the cache.
    /// </summary>
    public static void ClearAll() => Cache.Clear();

    /// <summary>
    /// Removes all cache entries whose key contains the given method name.
    /// </summary>
    /// <param name="methodName">Substring to match against cache keys (e.g. <c>"GetProduct"</c>).</param>
    public static void Invalidate(string methodName)
    {
        foreach (var k in Cache.Keys)
            if (k.Contains(methodName)) Cache.TryRemove(k, out _);
    }
}
