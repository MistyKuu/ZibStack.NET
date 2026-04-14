using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in aspect that records method execution metrics using <see cref="System.Diagnostics.Metrics"/>.
/// Tracks call count, duration histogram, and active (in-flight) calls for each decorated method.
///
/// <para>
/// The meter name defaults to <c>"ZibStack.Aop"</c> and can be overridden with <see cref="MeterName"/>.
/// Instrument names are derived from the class and method name (e.g. <c>"OrderService.GetOrder.duration"</c>)
/// or from <see cref="MetricName"/> when specified.
/// </para>
///
/// <para>
/// Emitted instruments (all three per method):
/// <list type="bullet">
///   <item><c>{name}.count</c> — Counter&lt;long&gt; incremented on each call</item>
///   <item><c>{name}.duration</c> — Histogram&lt;double&gt; recording elapsed milliseconds</item>
///   <item><c>{name}.errors</c> — Counter&lt;long&gt; incremented on exception</item>
/// </list>
/// These integrate with any <c>System.Diagnostics.Metrics</c>-compatible exporter (OpenTelemetry,
/// Prometheus, Application Insights, <c>dotnet-counters</c>, etc.).
/// </para>
///
/// <para>
/// The built-in <see cref="MetricsHandler"/> is registered automatically by
/// <c>AddAop()</c>. You do not need to register it by hand.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [Metrics]
/// public Order GetOrder(int id) { ... }
///
/// // Custom metric name:
/// [Metrics(MetricName = "checkout.place_order")]
/// public Task&lt;Order&gt; PlaceOrderAsync(OrderRequest request) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(MetricsHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class MetricsAttribute : AspectAttribute
{
    /// <summary>
    /// Override the meter name. Defaults to <c>"ZibStack.Aop"</c>.
    /// </summary>
    public string? MeterName { get; set; }

    /// <summary>
    /// Override the base instrument name. Defaults to <c>"ClassName.MethodName"</c>.
    /// The handler appends <c>.count</c>, <c>.duration</c>, and <c>.errors</c> suffixes.
    /// </summary>
    public string? MetricName { get; set; }
}
