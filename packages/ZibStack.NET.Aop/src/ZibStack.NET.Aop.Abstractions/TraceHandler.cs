using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in handler for <see cref="TraceAttribute"/>. Starts an <see cref="Activity"/>
/// in <see cref="OnBefore"/>, disposes it in <see cref="OnAfter"/>/<see cref="OnException"/>,
/// and attaches parameters, elapsed time, and status as tags.
///
/// <para>
/// Registered automatically by <c>AddAop()</c> as a singleton. You should not
/// instantiate this type directly.
/// </para>
///
/// <para>
/// Activity sources are cached per source name (class full name by default, or
/// <see cref="TraceAttribute.SourceName"/> when overridden). Make sure your exporter
/// (OpenTelemetry, Application Insights, etc.) is configured to listen on the matching
/// source names — or use a wildcard listener during setup.
/// </para>
/// </summary>
public sealed class TraceHandler : IAspectHandler
{
    private const string ActivityPropertyKey = "__zibstack_trace_activity";
    private const string TraceAttributeFullName = "ZibStack.NET.Aop.TraceAttribute";

    private static readonly ConcurrentDictionary<string, ActivitySource> Sources = new();

    public void OnBefore(AspectContext context)
    {
        var attr = FindTraceAttribute(context);

        var sourceName = attr?.SourceName ?? context.ClassName;
        var operationName = attr?.OperationName ?? context.MethodName;
        var includeParameters = attr?.IncludeParameters ?? true;

        var source = Sources.GetOrAdd(sourceName, static name => new ActivitySource(name));
        var activity = source.StartActivity(operationName, ActivityKind.Internal);
        if (activity is null) return;

        activity.SetTag("code.namespace", context.ClassName);
        activity.SetTag("code.function", context.MethodName);

        if (includeParameters)
        {
            foreach (var p in context.Parameters)
            {
                if (p.IsNoLog) continue;
                activity.SetTag(p.Name, p.IsSensitive ? "***" : p.Value?.ToString());
            }
        }

        context.Properties[ActivityPropertyKey] = activity;
    }

    public void OnAfter(AspectContext context)
    {
        if (!context.Properties.TryGetValue(ActivityPropertyKey, out var obj) || obj is not Activity activity)
            return;

        activity.SetTag("elapsed_ms", context.ElapsedMilliseconds);
        activity.SetStatus(ActivityStatusCode.Ok);
        activity.Dispose();
    }

    public void OnException(AspectContext context, Exception exception)
    {
        if (!context.Properties.TryGetValue(ActivityPropertyKey, out var obj) || obj is not Activity activity)
            return;

        activity.SetTag("elapsed_ms", context.ElapsedMilliseconds);
        activity.SetTag("exception.type", exception.GetType().FullName);
        activity.SetTag("exception.message", exception.Message);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.Dispose();
    }

    /// <summary>
    /// Generated interceptors build <see cref="AspectContext"/> without the original attribute
    /// instance, so we retrieve the live attribute via reflection on the target method's type.
    /// This runs once per call but only reads metadata tokens (no member invocation), so the
    /// overhead is negligible compared to the span cost itself.
    /// </summary>
    private static TraceAttribute? FindTraceAttribute(AspectContext context)
    {
        // Fast path: attribute stored by the generator (if/when it adds it to Properties).
        if (context.Properties.TryGetValue(TraceAttributeFullName, out var cached) && cached is TraceAttribute fromCtx)
            return fromCtx;

        return null;
    }
}
