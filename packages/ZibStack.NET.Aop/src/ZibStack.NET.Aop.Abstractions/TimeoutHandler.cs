using System;
using System.Threading.Tasks;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in handler for <see cref="TimeoutAttribute"/>. Enforces a maximum execution time
/// on the decorated method and throws <see cref="TimeoutException"/> if exceeded.
///
/// <para>
/// Registered automatically by <c>AddAop()</c> as a singleton.
/// </para>
/// </summary>
public sealed class TimeoutHandler : IAsyncAroundAspectHandler
{
    /// <inheritdoc />
    public async ValueTask<object?> AroundAsync(AspectContext context, Func<ValueTask<object?>> proceed)
    {
        var timeoutMs = 30_000;
        if (context.Properties.TryGetValue("TimeoutMs", out var tm) && tm is int t) timeoutMs = t;

        var work = proceed();
        var delay = Task.Delay(timeoutMs);

        var completed = await Task.WhenAny(work.AsTask(), delay).ConfigureAwait(false);

        if (completed == delay)
            throw new TimeoutException(
                $"{context.ClassName}.{context.MethodName} did not complete within {timeoutMs}ms.");

        return await work;
    }
}
