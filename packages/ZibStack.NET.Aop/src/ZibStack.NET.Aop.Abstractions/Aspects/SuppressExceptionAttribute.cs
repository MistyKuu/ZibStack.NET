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
/// // Option 1 — DI:
/// builder.Services.AddSingleton&lt;IExceptionObserver, MyExceptionLogger&gt;();
/// builder.Services.AddTransient&lt;SuppressExceptionHandler&gt;();
///
/// // Option 2 — static event:
/// SuppressExceptionHandler.OnExceptionSuppressed += (ctx, ex)
///     =&gt; Console.WriteLine($"Suppressed {ex.GetType().Name} in {ctx.MethodName}");
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
/// Receives exception notifications from <see cref="SuppressExceptionHandler"/>.
/// Register your implementation in DI to observe suppressed exceptions.
/// </summary>
public interface IExceptionObserver
{
    void OnException(AspectContext context, Exception exception);
}

/// <summary>
/// Built-in exception suppression handler. Uses <see cref="IExceptionObserver"/> from DI when available,
/// otherwise falls back to <see cref="OnExceptionSuppressed"/> static event.
/// Note: Cannot actually prevent re-throw in current pipeline — observability only.
/// </summary>
public sealed class SuppressExceptionHandler : IAspectHandler
{
    private readonly IExceptionObserver? _observer;

    /// <summary>Static event fallback. Used when DI is not configured.</summary>
    public static event Action<AspectContext, Exception>? OnExceptionSuppressed;

    public SuppressExceptionHandler() { }

    public SuppressExceptionHandler(IExceptionObserver observer) => _observer = observer;

    public void OnBefore(AspectContext context) { }
    public void OnAfter(AspectContext context) { }

    public void OnException(AspectContext context, Exception exception)
    {
        if (_observer != null)
            _observer.OnException(context, exception);
        else
            OnExceptionSuppressed?.Invoke(context, exception);
    }
}
