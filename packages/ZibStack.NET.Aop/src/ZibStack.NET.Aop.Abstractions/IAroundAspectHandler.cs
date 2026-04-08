namespace ZibStack.NET.Aop;

/// <summary>
/// Aspect handler that wraps the entire method execution. The handler receives a
/// <c>proceed</c> delegate that calls the original method — it can call it zero, one,
/// or multiple times, enabling patterns like caching, retry, authorization, and circuit breaking.
/// </summary>
/// <example>
/// <code>
/// public class CacheHandler : IAroundAspectHandler
/// {
///     public object? Around(AspectContext ctx, Func&lt;object?&gt; proceed)
///     {
///         var key = $"{ctx.MethodName}:{ctx.FormatParameters()}";
///         if (_cache.TryGetValue(key, out var cached)) return cached;
///         var result = proceed();
///         _cache.Set(key, result);
///         return result;
///     }
/// }
/// </code>
/// </example>
public interface IAroundAspectHandler
{
    /// <summary>
    /// Wraps the method execution. Call <paramref name="proceed"/> to invoke the original method.
    /// Return the method's return value (or a cached/transformed value).
    /// For void methods, return null and call proceed() for side effects.
    /// </summary>
    object? Around(AspectContext context, Func<object?> proceed);
}

/// <summary>
/// Strongly-typed version of <see cref="IAroundAspectHandler"/>. The generator will use
/// this when the handler's type parameter matches the intercepted method's return type.
/// Falls back to the non-generic version for void methods or type mismatches.
/// </summary>
/// <typeparam name="T">The return type of the intercepted method.</typeparam>
public interface IAroundAspectHandler<T>
{
    /// <summary>
    /// Wraps the method execution with strongly-typed proceed and return value.
    /// </summary>
    T? Around(AspectContext context, Func<T?> proceed);
}
