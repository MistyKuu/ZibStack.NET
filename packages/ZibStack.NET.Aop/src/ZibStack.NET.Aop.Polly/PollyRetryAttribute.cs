using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in aspect that retries method execution using Polly's <c>ResiliencePipeline</c>.
/// Provides richer retry policies than <see cref="RetryAttribute"/> — exponential/linear backoff,
/// jitter, named pipeline references, and integration with Polly's ecosystem (circuit breaker,
/// hedging, etc.).
///
/// <para>
/// Requires the <c>Polly.Core</c> NuGet package. Optionally add <c>Microsoft.Extensions.Resilience</c>
/// for DI integration with named pipelines.
/// </para>
///
/// <para>
/// <b>Two modes:</b>
/// <list type="bullet">
///   <item>
///     <b>Inline</b> (default) — the handler builds a retry pipeline from attribute properties
///     (<see cref="MaxRetryAttempts"/>, <see cref="DelayMs"/>, <see cref="BackoffType"/>).
///   </item>
///   <item>
///     <b>Named pipeline</b> — set <see cref="PipelineName"/> to reference a pipeline pre-configured
///     via <c>builder.Services.AddResiliencePipeline("name", ...)</c>. All other properties are ignored.
///   </item>
/// </list>
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Inline: exponential backoff, 3 retries, 200ms base delay
/// [PollyRetry(MaxRetryAttempts = 3, DelayMs = 200, BackoffType = RetryBackoffType.Exponential)]
/// public async Task&lt;Order&gt; GetOrderAsync(int id) { ... }
///
/// // Named pipeline: use a pre-configured Polly pipeline from DI
/// [PollyRetry(PipelineName = "external-api")]
/// public async Task&lt;string&gt; CallExternalAsync(string url) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(PollyRetryHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PollyRetryAttribute : AspectAttribute
{
    /// <summary>
    /// Maximum number of retry attempts (not counting the initial call). Default: 3.
    /// Ignored when <see cref="PipelineName"/> is set.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay in milliseconds between retries. Default: 200.
    /// Ignored when <see cref="PipelineName"/> is set.
    /// </summary>
    public int DelayMs { get; set; } = 200;

    /// <summary>
    /// Backoff strategy. Default: <see cref="RetryBackoffType.Exponential"/>.
    /// Ignored when <see cref="PipelineName"/> is set.
    /// </summary>
    public RetryBackoffType BackoffType { get; set; } = RetryBackoffType.Exponential;

    /// <summary>
    /// When set, the handler resolves a named <c>ResiliencePipeline</c> from
    /// <c>ResiliencePipelineProvider&lt;string&gt;</c> in DI instead of building one
    /// from attribute properties. This gives full control over retry strategy,
    /// circuit breaking, hedging, rate limiting, etc.
    /// </summary>
    public string? PipelineName { get; set; }

    /// <summary>
    /// Opt-in: only retry on these exception types.
    /// When set, exceptions not assignable to any listed type are rethrown immediately.
    /// <example><c>Handle = new[] { typeof(HttpRequestException), typeof(TimeoutException) }</c></example>
    /// Ignored when <see cref="PipelineName"/> is set.
    /// </summary>
    public Type[]? Handle { get; set; }

    /// <summary>
    /// Opt-out: never retry on these exception types.
    /// Exceptions assignable to any listed type are rethrown immediately, all others are retried.
    /// <example><c>Ignore = new[] { typeof(ArgumentException) }</c></example>
    /// Ignored when <see cref="Handle"/> or <see cref="PipelineName"/> is set.
    /// </summary>
    public Type[]? Ignore { get; set; }
}

/// <summary>
/// Backoff strategy for <see cref="PollyRetryAttribute"/>.
/// </summary>
public enum RetryBackoffType
{
    /// <summary>Constant delay between retries.</summary>
    Constant = 0,

    /// <summary>Delay increases linearly (delay * attempt).</summary>
    Linear = 1,

    /// <summary>Delay doubles each attempt (delay * 2^attempt) with jitter. Default.</summary>
    Exponential = 2,
}
