using ZibStack.NET.Aop;

namespace ZibStack.NET.Aop.Sample.Aspects;

[AspectHandler(typeof(TimingHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class TimingAttribute : AspectAttribute { }

public sealed class TimingHandler : IAspectHandler
{
    private readonly ILogger<TimingHandler> _logger;

    public TimingHandler(ILogger<TimingHandler> logger) => _logger = logger;

    public void OnBefore(AspectContext context) { }

    public void OnAfter(AspectContext context)
    {
        _logger.LogInformation("[Timing] {Class}.{Method} completed in {Ms}ms",
            context.ClassName, context.MethodName, context.ElapsedMilliseconds);
    }

    public void OnException(AspectContext context, Exception exception)
    {
        _logger.LogWarning("[Timing] {Class}.{Method} failed after {Ms}ms",
            context.ClassName, context.MethodName, context.ElapsedMilliseconds);
    }
}
