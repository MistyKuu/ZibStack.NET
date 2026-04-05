using System.Diagnostics;

namespace ZibStack.NET.Aop.Aspects;

/// <summary>
/// Creates an OpenTelemetry-compatible <see cref="Activity"/> span for the method.
/// Works with any OpenTelemetry exporter (Jaeger, Zipkin, OTLP, Application Insights).
/// The activity source name defaults to the class name.
/// </summary>
/// <example>
/// <code>
/// [Trace]
/// public async Task&lt;Order&gt; GetOrderAsync(int id) { ... }
///
/// // Custom activity source name:
/// [Trace(SourceName = "MyApp.Orders")]
/// public Order PlaceOrder(int customerId) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(TraceHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class TraceAttribute : AspectAttribute
{
    /// <summary>
    /// Activity source name. If null, uses "{ClassName}" as source name.
    /// </summary>
    public string? SourceName { get; set; }
}

/// <summary>
/// Built-in tracing handler. Creates an Activity span using System.Diagnostics.ActivitySource.
/// Compatible with OpenTelemetry — export spans to Jaeger, Zipkin, OTLP, etc.
/// </summary>
public sealed class TraceHandler : IAspectHandler
{
    private static readonly Dictionary<string, ActivitySource> _sources = new();

    public void OnBefore(AspectContext context)
    {
        var sourceName = context.Properties.ContainsKey("__trace_source")
            ? (string)context.Properties["__trace_source"]!
            : context.ClassName;

        if (!_sources.TryGetValue(sourceName, out var source))
        {
            source = new ActivitySource(sourceName);
            _sources[sourceName] = source;
        }

        var activity = source.StartActivity(context.MethodName, ActivityKind.Internal);
        if (activity != null)
        {
            // Add parameters as tags
            foreach (var p in context.Parameters)
            {
                if (p.IsNoLog) continue;
                activity.SetTag(p.Name, p.IsSensitive ? "***" : p.Value?.ToString());
            }
            context.Properties["__trace_activity"] = activity;
        }
    }

    public void OnAfter(AspectContext context)
    {
        if (context.Properties.TryGetValue("__trace_activity", out var obj) && obj is Activity activity)
        {
            activity.SetTag("elapsed_ms", context.ElapsedMilliseconds);
            activity.SetStatus(ActivityStatusCode.Ok);
            activity.Dispose();
        }
    }

    public void OnException(AspectContext context, Exception exception)
    {
        if (context.Properties.TryGetValue("__trace_activity", out var obj) && obj is Activity activity)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.type", exception.GetType().FullName },
                { "exception.message", exception.Message }
            }));
            activity.Dispose();
        }
    }
}
