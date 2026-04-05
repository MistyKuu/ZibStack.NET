using ZibStack.NET.Aop;

namespace ZibStack.NET.Aop.Sample.Aspects;

/// <summary>
/// Simple timing aspect — logs elapsed time via IAspectHandler.
/// </summary>
[AspectHandler(typeof(TimingHandler))]
public class TimingAttribute : AspectAttribute
{
}

public class TimingHandler : IAspectHandler
{
    public void OnBefore(AspectContext context)
    {
        Console.WriteLine($"[Timing] Starting {context.ClassName}.{context.MethodName}({context.FormatParameters()})");
    }

    public void OnAfter(AspectContext context)
    {
        Console.WriteLine($"[Timing] Completed {context.ClassName}.{context.MethodName} in {context.ElapsedMilliseconds}ms");
    }

    public void OnException(AspectContext context, Exception exception)
    {
        Console.WriteLine($"[Timing] Failed {context.ClassName}.{context.MethodName} after {context.ElapsedMilliseconds}ms: {exception.Message}");
    }
}
