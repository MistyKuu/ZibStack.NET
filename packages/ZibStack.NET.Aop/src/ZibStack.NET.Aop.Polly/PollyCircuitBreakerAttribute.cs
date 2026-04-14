using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Polly-based circuit breaker aspect. After <see cref="FailureThreshold"/> failures within
/// <see cref="SamplingDurationSeconds"/>, the circuit opens and subsequent calls fail
/// immediately with <see cref="Polly.CircuitBreaker.BrokenCircuitException"/> for
/// <see cref="BreakDurationSeconds"/>. Then half-opens to probe recovery.
/// </summary>
/// <example>
/// <code>
/// [PollyCircuitBreaker(FailureThreshold = 0.5, SamplingDurationSeconds = 30, BreakDurationSeconds = 15)]
/// public async Task&lt;string&gt; CallExternalApiAsync() { ... }
/// </code>
/// </example>
[AspectHandler(typeof(PollyCircuitBreakerHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PollyCircuitBreakerAttribute : AspectAttribute
{
    /// <summary>
    /// Failure ratio (0.0–1.0) that triggers the circuit. Default: 0.5 (50%).
    /// </summary>
    public double FailureThreshold { get; set; } = 0.5;

    /// <summary>
    /// Minimum number of calls in the sampling window before the failure ratio is evaluated. Default: 10.
    /// </summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>
    /// Sampling window in seconds. Default: 30.
    /// </summary>
    public int SamplingDurationSeconds { get; set; } = 30;

    /// <summary>
    /// How long the circuit stays open before half-opening, in seconds. Default: 15.
    /// </summary>
    public int BreakDurationSeconds { get; set; } = 15;
}
