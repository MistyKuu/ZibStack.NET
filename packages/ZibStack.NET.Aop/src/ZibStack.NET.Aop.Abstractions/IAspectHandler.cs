namespace ZibStack.NET.Aop;

/// <summary>
/// Runtime handler for custom aspects. Implement this interface and link it
/// to an attribute via <see cref="AspectHandlerAttribute"/>.
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
