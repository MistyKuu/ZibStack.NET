using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in handler for <see cref="MetricsAttribute"/>. Records method call metrics using
/// <see cref="System.Diagnostics.Metrics"/> following .NET and OpenTelemetry conventions.
///
/// <para>
/// Instruments are created once at construction time and shared across all decorated methods.
/// Method identity is captured via <c>aop.class</c> and <c>aop.method</c> tags on each measurement.
/// </para>
///
/// <para>
/// Emitted instruments:
/// <list type="bullet">
///   <item><c>aop.method.call.count</c> — Counter&lt;long&gt;, incremented on each call</item>
///   <item><c>aop.method.call.duration</c> — Histogram&lt;double&gt;, elapsed milliseconds per call</item>
///   <item><c>aop.method.call.errors</c> — Counter&lt;long&gt;, incremented on exception</item>
/// </list>
/// Override the meter name with <see cref="MetricsAttribute.MeterName"/>.
/// </para>
///
/// <para>
/// Integrates with any <c>System.Diagnostics.Metrics</c>-compatible exporter (OpenTelemetry,
/// Prometheus, Application Insights, <c>dotnet-counters</c>, etc.).
/// Registered automatically by <c>AddAop()</c> as a singleton.
/// </para>
/// </summary>
public sealed class MetricsHandler : IAspectHandler
{
    public const string DefaultMeterName = "ZibStack.Aop";

    private readonly Meter _meter;
    private readonly Counter<long> _callCount;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _errorCount;

    /// <summary>
    /// Creates a handler using <see cref="IMeterFactory"/> (recommended — integrates with DI lifecycle).
    /// </summary>
    public MetricsHandler(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(DefaultMeterName);
        _callCount = _meter.CreateCounter<long>("aop.method.call.count", description: "Number of aspect-decorated method calls");
        _duration = _meter.CreateHistogram<double>("aop.method.call.duration", "ms", "Method call duration in milliseconds");
        _errorCount = _meter.CreateCounter<long>("aop.method.call.errors", description: "Number of failed aspect-decorated method calls");
    }

    /// <summary>
    /// Creates a handler without <see cref="IMeterFactory"/> (fallback).
    /// </summary>
    public MetricsHandler()
    {
        _meter = new Meter(DefaultMeterName);
        _callCount = _meter.CreateCounter<long>("aop.method.call.count", description: "Number of aspect-decorated method calls");
        _duration = _meter.CreateHistogram<double>("aop.method.call.duration", "ms", "Method call duration in milliseconds");
        _errorCount = _meter.CreateCounter<long>("aop.method.call.errors", description: "Number of failed aspect-decorated method calls");
    }

    /// <inheritdoc />
    public void OnBefore(AspectContext context)
    {
        var tags = BuildTags(context);
        _callCount.Add(1, tags);
    }

    /// <inheritdoc />
    public void OnAfter(AspectContext context)
    {
        var tags = BuildTags(context);
        _duration.Record(context.ElapsedMilliseconds, tags);
    }

    /// <inheritdoc />
    public void OnException(AspectContext context, Exception exception)
    {
        var tags = BuildTags(context);
        _duration.Record(context.ElapsedMilliseconds, tags);
        _errorCount.Add(1, tags);
    }

    private static TagList BuildTags(AspectContext context)
    {
        var tags = new TagList
        {
            { "aop.class", context.ClassName },
            { "aop.method", context.MethodName },
        };

        // Allow user-specified metric name as an additional tag for custom grouping
        if (context.Properties.TryGetValue("MetricName", out var mn) && mn is string name && name.Length > 0)
            tags.Add("aop.metric", name);

        return tags;
    }
}
