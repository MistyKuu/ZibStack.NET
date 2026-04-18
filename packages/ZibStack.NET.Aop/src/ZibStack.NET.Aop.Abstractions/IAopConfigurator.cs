using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Project-wide fluent configuration for ZibStack.NET.Aop aspects.
/// Implement in a sealed class — the Aop source generator discovers implementations
/// in your compilation and parses the <see cref="Configure"/> method body as a
/// compile-time DSL. The method is <b>never invoked at runtime</b>.
///
/// <para>
/// Project-wide defaults are merged into every aspect instance at code-generation
/// time. Explicit attribute arguments always win — <c>[Retry(MaxAttempts = 5)]</c>
/// overrides a project default of <c>10</c>, but a bare <c>[Retry]</c> picks up
/// the project default.
/// </para>
/// </summary>
/// <example>
/// <code>
/// public sealed class AopConfig : IAopConfigurator
/// {
///     public void Configure(IAopBuilder b)
///     {
///         b.Retry(r => { r.MaxAttempts = 5; r.DelayMs = 200; });
///         b.Timeout(t => t.TimeoutMs = 10_000);
///         b.Trace(t => t.IncludeParameters = false);
///         b.Cache(c => c.DurationSeconds = 600);
///         b.Metrics(m => m.MeterName = "checkout.aop");
///     }
/// }
/// </code>
/// </example>
public interface IAopConfigurator
{
    void Configure(IAopBuilder b);
}

/// <summary>Fluent builder for project-wide aspect defaults and bulk application. Chain sections; per-method attribute arguments win.</summary>
public interface IAopBuilder
{
    IAopBuilder Retry(Action<RetryDefaults> configure);
    IAopBuilder Timeout(Action<TimeoutDefaults> configure);
    IAopBuilder Trace(Action<TraceDefaults> configure);
    IAopBuilder Cache(Action<CacheDefaults> configure);
    IAopBuilder Metrics(Action<MetricsDefaults> configure);

    /// <summary>
    /// Bulk-apply an aspect to all methods matching the selector. Equivalent to placing
    /// <c>[TAspect]</c> on every matching method — but driven from a central config file
    /// instead of individual attributes.
    /// <code>
    /// b.Apply&lt;CacheAttribute&gt;(to => to
    ///     .Namespace("MyApp.Services")
    ///     .Implementing&lt;IRepository&gt;()
    ///     .PublicMethods()
    /// );
    /// </code>
    /// </summary>
    /// <typeparam name="TAspect">Aspect attribute type (must derive from <see cref="AspectAttribute"/>).</typeparam>
    /// <param name="selector">Fluent selector chain narrowing which classes/methods receive the aspect.</param>
    IAopBuilder Apply<TAspect>(Action<IAspectSelector> selector) where TAspect : AspectAttribute;

    /// <summary>
    /// Bulk-apply an aspect with configuration to all methods matching the selector.
    /// <code>
    /// b.Apply&lt;RetryAttribute&gt;(to => to
    ///     .Implementing&lt;IExternalService&gt;()
    /// , r => r.MaxAttempts = 5);
    /// </code>
    /// </summary>
    IAopBuilder Apply<TAspect>(Action<IAspectSelector> selector, Action<TAspect> configure) where TAspect : AspectAttribute;
}

/// <summary>Defaults merged into every <c>[Retry]</c> attribute.</summary>
public sealed class RetryDefaults
{
    public int MaxAttempts { get; set; } = 3;
    public int DelayMs { get; set; }
    public double BackoffMultiplier { get; set; } = 1.0;
}

/// <summary>Defaults merged into every <c>[Timeout]</c> attribute.</summary>
public sealed class TimeoutDefaults
{
    public int TimeoutMs { get; set; } = 30_000;
}

/// <summary>Defaults merged into every <c>[Trace]</c> attribute.</summary>
public sealed class TraceDefaults
{
    /// <summary>Default ActivitySource name. Null → fall back to the attribute/handler default.</summary>
    public string? SourceName { get; set; }
    public bool IncludeParameters { get; set; } = true;
}

/// <summary>Defaults merged into every <c>[Cache]</c> attribute.</summary>
public sealed class CacheDefaults
{
    public int DurationSeconds { get; set; } = 300;
}

/// <summary>Defaults merged into every <c>[Metrics]</c> attribute.</summary>
public sealed class MetricsDefaults
{
    /// <summary>Default meter name. Null → fall back to the attribute/handler default (<c>ZibStack.Aop</c>).</summary>
    public string? MeterName { get; set; }
}
