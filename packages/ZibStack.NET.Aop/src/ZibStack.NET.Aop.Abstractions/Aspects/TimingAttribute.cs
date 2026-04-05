namespace ZibStack.NET.Aop.Aspects;

/// <summary>
/// Records method execution time. Calls <see cref="TimingHandler.OnTimingRecorded"/>
/// static event after each method call with the class name, method name, and elapsed time.
/// Useful for lightweight metrics without full OpenTelemetry setup.
/// </summary>
/// <example>
/// <code>
/// // Subscribe to timing events:
/// TimingHandler.OnTimingRecorded += (className, methodName, elapsedMs)
///     => Console.WriteLine($"{className}.{methodName}: {elapsedMs}ms");
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
/// Built-in timing handler. Raises <see cref="OnTimingRecorded"/> event with elapsed time.
/// </summary>
public sealed class TimingHandler : IAspectHandler
{
    /// <summary>
    /// Event raised after each method call. Parameters: className, methodName, elapsedMilliseconds.
    /// Subscribe to this to record metrics.
    /// </summary>
    public static event Action<string, string, long>? OnTimingRecorded;

    public void OnBefore(AspectContext context) { }

    public void OnAfter(AspectContext context)
    {
        OnTimingRecorded?.Invoke(context.ClassName, context.MethodName, context.ElapsedMilliseconds);
    }

    public void OnException(AspectContext context, Exception exception)
    {
        OnTimingRecorded?.Invoke(context.ClassName, context.MethodName, context.ElapsedMilliseconds);
    }
}
