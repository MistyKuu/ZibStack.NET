namespace ZibStack.NET.Aop;

/// <summary>
/// Synchronous runtime handler for custom aspects. Implement this interface and link it
/// to an attribute via <see cref="AspectHandlerAttribute"/>.
/// Works on both sync and async methods.
/// For async hooks, implement <see cref="IAsyncAspectHandler"/> (async methods only).
/// </summary>
public interface IAspectHandler
{
    /// <summary>Called before the original method executes.</summary>
    void OnBefore(AspectContext context);

    /// <summary>Called after the original method returns successfully.</summary>
    void OnAfter(AspectContext context);

    /// <summary>Called when the original method throws. The exception is re-thrown after all handlers run.</summary>
    void OnException(AspectContext context, Exception exception);
}
