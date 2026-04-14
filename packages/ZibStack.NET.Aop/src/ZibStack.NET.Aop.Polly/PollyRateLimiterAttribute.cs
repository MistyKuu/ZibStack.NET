using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Polly-based rate limiter aspect. Uses a fixed window rate limiter to restrict
/// how many times a method can be called within a time window.
/// Excess calls throw <see cref="Polly.RateLimiting.RateLimiterRejectedException"/>.
/// </summary>
/// <example>
/// <code>
/// // Max 100 calls per 60 seconds:
/// [PollyRateLimiter(PermitLimit = 100, WindowSeconds = 60)]
/// public async Task&lt;SearchResult&gt; SearchAsync(string query) { ... }
///
/// // Max 5 calls per second (burst protection):
/// [PollyRateLimiter(PermitLimit = 5, WindowSeconds = 1)]
/// public async Task SendNotificationAsync(int userId) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(PollyRateLimiterHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PollyRateLimiterAttribute : AspectAttribute
{
    /// <summary>
    /// Maximum number of calls allowed in the window. Default: 100.
    /// </summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>
    /// Window duration in seconds. Default: 60.
    /// </summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum number of calls that can be queued when the limit is reached.
    /// Queued calls wait for the next window. Default: 0 (reject immediately).
    /// </summary>
    public int QueueLimit { get; set; }
}
