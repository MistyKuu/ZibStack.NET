using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in aspect that caches method return values in memory. The cache key is derived from
/// the class name, method name, and parameter values (respecting <c>[Sensitive]</c> masking
/// and <c>[NoLog]</c> exclusion via <see cref="AspectContext.FormatParameters"/>).
///
/// <para>
/// Set <see cref="DurationSeconds"/> to control TTL. Use <c>0</c> for infinite caching.
/// Use <see cref="CacheHandler.Invalidate"/> or <see cref="CacheHandler.ClearAll"/> to
/// manually evict entries when needed.
/// </para>
///
/// <para>
/// The built-in <see cref="CacheHandler"/> is registered automatically by
/// <c>AddAop()</c>. You do not need to register it by hand.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [Cache(DurationSeconds = 60)]
/// public Product GetProduct(int id) { ... }
///
/// // Custom cache key with nested property access:
/// [Cache(KeyTemplate = "order:{order.Customer.Id}:{order.Status}")]
/// public Invoice GetInvoice(Order order, bool draft) { ... }
///
/// // Infinite cache (until manual invalidation):
/// [Cache(DurationSeconds = 0)]
/// public IReadOnlyList&lt;Country&gt; GetCountries() { ... }
/// </code>
/// </example>
[AspectHandler(typeof(CacheHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class CacheAttribute : AspectAttribute
{
    /// <summary>
    /// Cache entry time-to-live in seconds. Default: 300 (5 minutes).
    /// Use <c>0</c> for infinite caching (until manual invalidation or app restart).
    /// </summary>
    public int DurationSeconds { get; set; } = 300;

    /// <summary>
    /// Custom cache key template with <c>{parameter}</c> placeholders.
    /// Supports nested property access (e.g. <c>"user:{user.Id}:{role}"</c>).
    /// The source generator resolves placeholders at compile time — invalid references
    /// produce compiler errors. When not set, the key is derived from
    /// <c>ClassName.MethodName(FormatParameters())</c>.
    /// </summary>
    public string? KeyTemplate { get; set; }
}
