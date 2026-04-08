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

/// <summary>
/// Strongly-typed async version of <see cref="IAsyncAroundAspectHandler"/>. The generator will use
/// this when the handler's type parameter matches the intercepted method's return type.
/// </summary>
/// <typeparam name="T">The return type of the intercepted async method (unwrapped from Task/ValueTask).</typeparam>
public interface IAsyncAroundAspectHandler<T>
{
    /// <summary>
    /// Wraps the async method execution with strongly-typed proceed and return value.
    /// </summary>
    ValueTask<T?> AroundAsync(AspectContext context, Func<ValueTask<T?>> proceed);
}
