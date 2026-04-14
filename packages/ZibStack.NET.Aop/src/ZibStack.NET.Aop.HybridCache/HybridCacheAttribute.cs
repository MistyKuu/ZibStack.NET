using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in aspect that caches method return values using <c>Microsoft.Extensions.Caching.Hybrid.HybridCache</c>.
/// Unlike <see cref="CacheAttribute"/> (which uses a simple in-memory dictionary), this aspect
/// integrates with the .NET HybridCache infrastructure and supports L1/L2 caching (memory + Redis, etc.).
///
/// <para>
/// Requires <c>Microsoft.Extensions.Caching.Hybrid</c> NuGet package and HybridCache registered in DI:
/// <code>builder.Services.AddHybridCache();</code>
/// </para>
///
/// <para>
/// The built-in <see cref="HybridCacheHandler"/> is registered automatically by
/// <c>AddAop()</c> when <c>HybridCache</c> is available in DI.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [HybridCache(DurationSeconds = 120)]
/// public async Task&lt;Product&gt; GetProductAsync(int id) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(HybridCacheHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class HybridCacheAttribute : AspectAttribute
{
    /// <summary>
    /// Cache entry time-to-live in seconds. Default: 300 (5 minutes).
    /// </summary>
    public int DurationSeconds { get; set; } = 300;

    /// <summary>
    /// Custom cache key template. See <see cref="CacheAttribute.KeyTemplate"/> for syntax.
    /// </summary>
    public string? KeyTemplate { get; set; }
}
