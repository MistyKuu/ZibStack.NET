using System.Collections.Concurrent;
using ZibStack.NET.Aop;

namespace ZibStack.NET.Aop.Sample.Aspects;

[AspectHandler(typeof(CacheHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class CacheAttribute : AspectAttribute
{
    public int DurationSeconds { get; set; } = 300;
}

public sealed class CacheHandler : IAroundAspectHandler
{
    private static readonly ConcurrentDictionary<string, (object? Value, DateTime Expiry)> _cache = new();

    public object? Around(AspectContext context, Func<object?> proceed)
    {
        var key = $"{context.ClassName}.{context.MethodName}({context.FormatParameters()})";
        var duration = 300;
        if (context.Properties.TryGetValue("DurationSeconds", out var ds) && ds is int d) duration = d;

        if (_cache.TryGetValue(key, out var entry) && (entry.Expiry == DateTime.MaxValue || entry.Expiry > DateTime.UtcNow))
            return entry.Value;

        var result = proceed();
        _cache[key] = (result, duration > 0 ? DateTime.UtcNow.AddSeconds(duration) : DateTime.MaxValue);
        return result;
    }

    public static void ClearAll() => _cache.Clear();
    public static void Invalidate(string method)
    {
        foreach (var k in _cache.Keys)
            if (k.Contains(method)) _cache.TryRemove(k, out _);
    }
}
