namespace ZibStack.NET.Aop.Aspects;

/// <summary>
/// Caches method return value based on parameter values via <see cref="IAspectCache"/> from DI.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddSingleton&lt;IAspectCache, MyMemoryCache&gt;();
/// builder.Services.AddTransient&lt;CacheHandler&gt;();
///
/// [Cache(DurationSeconds = 60)]
/// public Order GetOrder(int id) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(CacheHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class CacheAttribute : AspectAttribute
{
    public int DurationSeconds { get; set; } = 300;
}

/// <summary>
/// Register in DI to provide caching for <see cref="CacheAttribute"/>.
/// Wrap IMemoryCache, IDistributedCache, Redis, etc.
/// </summary>
public interface IAspectCache
{
    bool TryGet(string key, out object? value);
    void Set(string key, object? value, int durationSeconds);
    void Remove(string key);
}

public sealed class CacheHandler : IAroundAspectHandler
{
    private readonly IAspectCache _cache;

    public CacheHandler(IAspectCache cache) => _cache = cache;

    public object? Around(AspectContext context, Func<object?> proceed)
    {
        var key = $"{context.ClassName}.{context.MethodName}({context.FormatParameters()})";
        var durationSeconds = 300;
        if (context.Properties.TryGetValue("DurationSeconds", out var ds) && ds is int d)
            durationSeconds = d;

        if (_cache.TryGet(key, out var cached))
            return cached;

        var result = proceed();
        _cache.Set(key, result, durationSeconds);
        return result;
    }
}
