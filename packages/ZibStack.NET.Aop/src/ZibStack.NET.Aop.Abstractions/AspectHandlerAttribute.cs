namespace ZibStack.NET.Aop;

/// <summary>
/// Links an aspect attribute to a runtime <see cref="IAspectHandler"/> implementation.
/// Place this on your custom aspect attribute class.
/// </summary>
/// <example>
/// <code>
/// [AspectHandler(typeof(MetricsHandler))]
/// public class MetricsAttribute : AspectAttribute
/// {
///     public string MetricName { get; set; } = "";
/// }
///
/// public class MetricsHandler : IAspectHandler
/// {
///     public void OnBefore(AspectContext ctx) { ... }
///     public void OnAfter(AspectContext ctx) { ... }
///     public void OnException(AspectContext ctx, Exception ex) { ... }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AspectHandlerAttribute : Attribute
{
    public Type HandlerType { get; }
    public AspectHandlerAttribute(Type handlerType) => HandlerType = handlerType;
}
