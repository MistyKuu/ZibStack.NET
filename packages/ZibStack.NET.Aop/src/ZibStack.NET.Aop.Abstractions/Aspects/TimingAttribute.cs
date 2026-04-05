namespace ZibStack.NET.Aop.Aspects;

/// <summary>
/// Records method execution time. Register a custom <see cref="ITimingRecorder"/>
/// in DI to receive timing data.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddSingleton&lt;ITimingRecorder, MyMetricsRecorder&gt;();
/// builder.Services.AddTransient&lt;TimingHandler&gt;();
/// AspectServiceProvider.ServiceProvider = app.Services;
///
/// [Timing]
/// public Order GetOrder(int id) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(TimingHandler))]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class TimingAttribute : AspectAttribute
{
}

/// <summary>
/// Receives timing data from <see cref="TimingHandler"/>.
/// Register your implementation in DI to receive metrics.
/// </summary>
public interface ITimingRecorder
{
    void Record(string className, string methodName, long elapsedMilliseconds);
}

/// <summary>
/// Built-in timing handler. Uses <see cref="ITimingRecorder"/> from DI to record metrics.
/// </summary>
public sealed class TimingHandler : IAspectHandler
{
    private readonly ITimingRecorder _recorder;

    public TimingHandler(ITimingRecorder recorder) => _recorder = recorder;

    public void OnBefore(AspectContext context) { }

    public void OnAfter(AspectContext context)
    {
        _recorder.Record(context.ClassName, context.MethodName, context.ElapsedMilliseconds);
    }

    public void OnException(AspectContext context, Exception exception)
    {
        _recorder.Record(context.ClassName, context.MethodName, context.ElapsedMilliseconds);
    }
}
