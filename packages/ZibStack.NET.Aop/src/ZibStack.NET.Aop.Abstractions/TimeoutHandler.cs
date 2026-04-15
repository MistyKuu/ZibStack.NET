using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in handler for <see cref="TimeoutAttribute"/>. Enforces a maximum execution
/// time on the decorated method and throws <see cref="TimeoutException"/> if exceeded.
///
/// <para>
/// When the decorated method declares a <see cref="CancellationToken"/> parameter, the
/// generator wires up an <see cref="AspectContext.CancellationTokenSource"/> linked to
/// the caller's token. This handler calls <see cref="CancellationTokenSource.CancelAfter(int)"/>
/// on it, so the body's awaits cooperatively abort the moment the deadline hits — no
/// background leak, no dangling work.
/// </para>
///
/// <para>
/// When the method has no <see cref="CancellationToken"/> parameter the handler still
/// trips a <see cref="TimeoutException"/> at the deadline (so the caller doesn't wait
/// past the limit), but the body has no way to observe cancellation and keeps running
/// until it finishes naturally. This is the case <c>AOP0015</c> warns about.
/// </para>
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

        // Cooperative path: the generator gave us a CTS linked to the method's CT param.
        // CancelAfter signals it; the body sees the cancellation through its own awaits
        // (e.g. await Task.Delay(ms, ct), HttpClient.GetAsync(url, ct)) and returns
        // promptly via OperationCanceledException, which we translate to TimeoutException.
        if (context.CancellationTokenSource is { } cts)
        {
            cts.CancelAfter(timeoutMs);
            try
            {
                return await proceed().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"{context.ClassName}.{context.MethodName} did not complete within {timeoutMs}ms.");
            }
        }

        // Non-cooperative path (no CT param on method): race proceed against a Task.Delay.
        // The caller sees TimeoutException promptly, but the body keeps running in the
        // background until natural completion — it has no token to observe.
        var work = proceed();
        var delay = Task.Delay(timeoutMs);
        var completed = await Task.WhenAny(work.AsTask(), delay).ConfigureAwait(false);

        if (completed == delay)
            throw new TimeoutException(
                $"{context.ClassName}.{context.MethodName} did not complete within {timeoutMs}ms.");

        return await work;
    }
}
