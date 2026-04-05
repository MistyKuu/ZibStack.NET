using System.Diagnostics;
using ZibStack.NET.Aop;

namespace ZibStack.NET.Aop.Sample.Aspects;

[AspectHandler(typeof(TraceHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class TraceAttribute : AspectAttribute
{
    public string? SourceName { get; set; }
}

public sealed class TraceHandler : IAspectHandler
{
    private static readonly Dictionary<string, ActivitySource> _sources = new();

    public void OnBefore(AspectContext context)
    {
        var sourceName = context.ClassName;
        if (!_sources.TryGetValue(sourceName, out var source))
        {
            source = new ActivitySource(sourceName);
            _sources[sourceName] = source;
        }
        var activity = source.StartActivity(context.MethodName, ActivityKind.Internal);
        if (activity != null)
        {
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
            activity.Dispose();
        }
    }
}
