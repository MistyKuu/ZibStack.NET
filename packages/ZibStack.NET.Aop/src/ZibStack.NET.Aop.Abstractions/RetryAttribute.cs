using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in aspect that retries a method on failure with optional exponential backoff.
/// Uses <see cref="IAroundAspectHandler"/> to wrap the method call in a retry loop.
///
/// <para>
/// On each failure (except the last attempt), the handler waits <see cref="DelayMs"/> milliseconds
/// before retrying, multiplying the delay by <see cref="BackoffMultiplier"/> after each failure.
/// The final attempt rethrows the original exception if it fails.
/// </para>
///
/// <para>
/// The built-in <see cref="RetryHandler"/> is registered automatically by
/// <c>AddAop()</c>. You do not need to register it by hand.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [Retry(MaxAttempts = 3, DelayMs = 200, BackoffMultiplier = 2.0)]
/// public async Task&lt;Order&gt; GetOrderFromExternalApiAsync(int id) { ... }
///
/// // Simple retry with defaults (3 attempts, no delay):
/// [Retry]
/// public string FetchData(string url) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(RetryHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RetryAttribute : AspectAttribute
{
    /// <summary>
    /// Maximum number of attempts (including the first call). Default: 3.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Initial delay in milliseconds between retries. Default: 0 (no delay).
    /// </summary>
    public int DelayMs { get; set; }

    /// <summary>
    /// Multiplier applied to <see cref="DelayMs"/> after each failed attempt.
    /// For example, <c>DelayMs = 100</c> and <c>BackoffMultiplier = 2.0</c> produces
    /// delays of 100ms, 200ms, 400ms, etc. Default: 1.0 (constant delay).
    /// </summary>
    public double BackoffMultiplier { get; set; } = 1.0;

    /// <summary>
    /// Opt-in: only retry on these exception types.
    /// When set, exceptions not assignable to any listed type are rethrown immediately.
    /// <example><c>Handle = new[] { typeof(HttpRequestException), typeof(TimeoutException) }</c></example>
    /// </summary>
    public Type[]? Handle { get; set; }

    /// <summary>
    /// Opt-out: never retry on these exception types.
    /// Exceptions assignable to any listed type are rethrown immediately, all others are retried.
    /// <example><c>Ignore = new[] { typeof(ArgumentException), typeof(ValidationException) }</c></example>
    /// Ignored when <see cref="Handle"/> is set.
    /// </summary>
    public Type[]? Ignore { get; set; }
}
