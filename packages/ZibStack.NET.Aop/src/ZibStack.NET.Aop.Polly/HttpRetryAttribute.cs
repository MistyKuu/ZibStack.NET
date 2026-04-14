using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in aspect that retries HTTP calls on transient failures using Polly.
/// Pre-configured to handle the same transient errors as
/// <c>Microsoft.Extensions.Http.Resilience</c>:
/// <list type="bullet">
///   <item><see cref="System.Net.Http.HttpRequestException"/></item>
///   <item><see cref="TaskCanceledException"/> (HTTP timeouts)</item>
///   <item>HTTP 408, 429, 500, 502, 503, 504 (when available via <c>HttpRequestException.StatusCode</c>)</item>
/// </list>
///
/// <para>
/// Requires the <c>Polly.Core</c> NuGet package.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [HttpRetry]
/// public async Task&lt;string&gt; CallApiAsync(string url) { ... }
///
/// [HttpRetry(MaxRetryAttempts = 5, DelayMs = 500)]
/// public async Task&lt;OrderResponse&gt; PlaceOrderAsync(OrderRequest req) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(PollyHttpRetryHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PollyHttpRetryAttribute : AspectAttribute
{
    /// <summary>
    /// Maximum number of retry attempts (not counting the initial call). Default: 3.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay in milliseconds between retries. Default: 200.
    /// </summary>
    public int DelayMs { get; set; } = 200;
}
