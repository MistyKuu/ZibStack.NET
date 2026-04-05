namespace ZibStack.NET.Aop.Aspects;

/// <summary>
/// Records method execution time. When using DI, register a custom <see cref="ITimingRecorder"/>
/// to receive timing data. Without DI, falls back to <see cref="TimingHandler.OnTimingRecorded"/> static event.
/// </summary>
/// <example>
/// <code>
/// // Option 1 — DI (recommended):
/// builder.Services.AddSingleton&lt;ITimingRecorder, MyMetricsRecorder&gt;();
/// builder.Services.AddTransient&lt;TimingHandler&gt;();
/// AspectServiceProvider.ServiceProvider = app.Services;
///
/// // Option 2 — static event (no DI needed):
/// TimingHandler.OnTimingRecorded += (className, methodName, elapsedMs)
///     =&gt; Console.WriteLine($"{className}.{methodName}: {elapsedMs}ms");
///
/// [Timing]
/// public Order GetOrder(int id) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(TimingHandler))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
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
/// Built-in timing handler. Uses <see cref="ITimingRecorder"/> from DI when available,
/// otherwise falls back to <see cref="OnTimingRecorded"/> static event.
/// </summary>
public sealed class TimingHandler : IAspectHandler
{
    private readonly ITimingRecorder? _recorder;

    /// <summary>
    /// Static event fallback. Used when DI is not configured or no <see cref="ITimingRecorder"/> is registered.
    /// </summary>
    public static event Action<string, string, long>? OnTimingRecorded;

    public TimingHandler() { }

    public TimingHandler(ITimingRecorder recorder) => _recorder = recorder;

    public void OnBefore(AspectContext context) { }

    public void OnAfter(AspectContext context)
    {
        Record(context);
    }

    public void OnException(AspectContext context, Exception exception)
    {
        Record(context);
    }

    private void Record(AspectContext context)
    {
        if (_recorder != null)
            _recorder.Record(context.ClassName, context.MethodName, context.ElapsedMilliseconds);
        else
            OnTimingRecorded?.Invoke(context.ClassName, context.MethodName, context.ElapsedMilliseconds);
    }
}
