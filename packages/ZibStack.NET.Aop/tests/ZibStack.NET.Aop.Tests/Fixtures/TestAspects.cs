using ZibStack.NET.Aop;
using ZibStack.NET.Log;

namespace ZibStack.NET.Aop.Tests.Fixtures;

// ── Before/After aspect ─────────────────────────────────────────────────────

[AspectHandler(typeof(RecordingHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true)]
public sealed class RecordAttribute : AspectAttribute { }

public sealed class RecordingHandler : IAspectHandler
{
    public List<(string Phase, AspectContext Context)> Calls { get; } = new();

    public void Reset() => Calls.Clear();

    public void OnBefore(AspectContext context) =>
        Calls.Add(("Before", context));

    public void OnAfter(AspectContext context) =>
        Calls.Add(("After", context));

    public void OnException(AspectContext context, Exception exception) =>
        Calls.Add(("Exception", context));
}

// ── Around aspect ───────────────────────────────────────────────────────────

[AspectHandler(typeof(AroundRecordingHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RecordAroundAttribute : AspectAttribute { }

public sealed class AroundRecordingHandler : IAroundAspectHandler
{
    public bool Called { get; private set; }
    public bool ProceedCalled { get; private set; }

    public void Reset()
    {
        Called = false;
        ProceedCalled = false;
    }

    public object? Around(AspectContext context, Func<object?> proceed)
    {
        Called = true;
        var result = proceed();
        ProceedCalled = true;
        return result;
    }
}
