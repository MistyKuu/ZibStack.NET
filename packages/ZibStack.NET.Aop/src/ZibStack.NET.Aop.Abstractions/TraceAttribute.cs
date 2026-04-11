using System;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in aspect that creates an <see cref="System.Diagnostics.Activity"/> span
/// for the decorated method, compatible with OpenTelemetry / Jaeger / Zipkin / OTLP exporters.
///
/// <para>
/// The generated <see cref="System.Diagnostics.ActivitySource"/> name defaults to the
/// declaring class's full name. Override with <see cref="SourceName"/>.
/// The span name defaults to the method name — override with <see cref="OperationName"/>.
/// </para>
///
/// <para>
/// Method parameters are attached as span tags by default. Parameters marked
/// <c>[Sensitive]</c> are masked as <c>***</c> and <c>[NoLog]</c> parameters are excluded.
/// Disable parameter tagging entirely with <see cref="IncludeParameters"/> = <c>false</c>.
/// </para>
///
/// <para>
/// The built-in <see cref="TraceHandler"/> is registered automatically by
/// <c>AddAop()</c>. You do not need to register it by hand.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Program.cs
/// builder.Services.AddAop();
/// var app = builder.Build();
/// app.Services.UseAop();
///
/// // Apply to any method or class:
/// [Trace]
/// public async Task&lt;Order&gt; GetOrderAsync(int id) { ... }
///
/// // Custom source name (groups spans under a logical service):
/// [Trace(SourceName = "checkout.orders")]
/// public Task PlaceOrderAsync(Order order) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(TraceHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class TraceAttribute : AspectAttribute
{
    /// <summary>
    /// Override the <see cref="System.Diagnostics.ActivitySource"/> name.
    /// Defaults to the declaring class's full name.
    /// </summary>
    public string? SourceName { get; set; }

    /// <summary>
    /// Override the span / operation name. Defaults to the method name.
    /// </summary>
    public string? OperationName { get; set; }

    /// <summary>
    /// When <c>true</c> (default), method parameters are attached as span tags,
    /// honoring <c>[Sensitive]</c> and <c>[NoLog]</c>. Set to <c>false</c> to skip
    /// parameter tags entirely for hot paths or wide signatures.
    /// </summary>
    public bool IncludeParameters { get; set; } = true;
}
