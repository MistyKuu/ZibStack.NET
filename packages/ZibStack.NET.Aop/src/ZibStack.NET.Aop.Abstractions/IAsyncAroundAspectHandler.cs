using System.Threading.Tasks;

namespace ZibStack.NET.Aop;

/// <summary>
/// Async version of <see cref="IAroundAspectHandler"/>. For async methods only.
/// </summary>
/// <example>
/// <code>
/// public class RetryHandler : IAsyncAroundAspectHandler
/// {
///     public async ValueTask&lt;object?&gt; AroundAsync(AspectContext ctx, Func&lt;ValueTask&lt;object?&gt;&gt; proceed)
///     {
///         for (int i = 0; i &lt; 3; i++)
///         {
///             try { return await proceed(); }
///             catch when (i &lt; 2) { await Task.Delay(100 * (i + 1)); }
///         }
///         return await proceed();
///     }
/// }
/// </code>
/// </example>
public interface IAsyncAroundAspectHandler
{
    /// <summary>
    /// Wraps the async method execution. Call <paramref name="proceed"/> to invoke the original method.
    /// </summary>
    ValueTask<object?> AroundAsync(AspectContext context, Func<ValueTask<object?>> proceed);
}
