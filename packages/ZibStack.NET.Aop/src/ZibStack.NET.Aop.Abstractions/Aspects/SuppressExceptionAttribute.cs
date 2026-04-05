namespace ZibStack.NET.Aop.Aspects;

/// <summary>
/// Catches exceptions and returns a default value instead of throwing.
/// Useful for fault-tolerant methods where a failure should return null/default.
/// Note: this handler runs in OnException but cannot prevent the re-throw in the current
/// AOP pipeline. This is a diagnostic/observability aspect — full exception suppression
/// requires an inline emitter (planned for v2).
/// </summary>
/// <example>
/// <code>
/// // Currently useful for observability — logs/records that an exception was suppressed:
/// SuppressExceptionHandler.OnExceptionSuppressed += (ctx, ex)
///     => Console.WriteLine($"Suppressed {ex.GetType().Name} in {ctx.MethodName}");
///
/// [SuppressException]
/// public Order? GetOrder(int id) { ... }
/// </code>
/// </example>
[AspectHandler(typeof(SuppressExceptionHandler))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SuppressExceptionAttribute : AspectAttribute
{
}

/// <summary>
/// Built-in exception suppression handler. Raises an event when exceptions occur.
/// Note: Cannot actually prevent re-throw in current pipeline — observability only.
/// </summary>
public sealed class SuppressExceptionHandler : IAspectHandler
{
    /// <summary>Event raised when an exception is caught. For observability.</summary>
    public static event Action<AspectContext, Exception>? OnExceptionSuppressed;

    public void OnBefore(AspectContext context) { }
    public void OnAfter(AspectContext context) { }

    public void OnException(AspectContext context, Exception exception)
    {
        OnExceptionSuppressed?.Invoke(context, exception);
    }
}
