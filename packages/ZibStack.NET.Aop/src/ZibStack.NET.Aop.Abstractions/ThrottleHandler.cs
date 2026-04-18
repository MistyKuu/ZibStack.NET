using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in handler for <see cref="ThrottleAttribute"/>. Limits execution to at
/// most once per interval. The first call in each window executes immediately;
/// subsequent calls are suppressed. When <c>Trailing</c> is enabled, the last
/// suppressed call fires when the interval expires.
///
/// <para>Registered automatically by <c>AddAop()</c> as a singleton.</para>
/// </summary>
public sealed class ThrottleHandler : IAsyncAroundAspectHandler
{
    private readonly ConcurrentDictionary<string, ThrottleState> _state =
        new ConcurrentDictionary<string, ThrottleState>();

    /// <inheritdoc />
    public async ValueTask<object?> AroundAsync(AspectContext context, Func<ValueTask<object?>> proceed)
    {
        var intervalMs = 1000;
        if (context.Properties.TryGetValue("IntervalMs", out var im) && im is int i) intervalMs = i;
        var trailing = true;
        if (context.Properties.TryGetValue("Trailing", out var tr) && tr is bool b) trailing = b;

        var key = BuildKey(context);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var state = _state.GetOrAdd(key, _ => new ThrottleState());

        lock (state)
        {
            var elapsed = now - state.LastExecutedAt;

            if (elapsed >= intervalMs || state.LastExecutedAt == 0)
            {
                // Interval has passed (or first call) — execute immediately.
                state.LastExecutedAt = now;
                state.PendingProceed = null;
                if (state.PendingCts != null)
                {
                    state.PendingCts.Cancel();
                    state.PendingCts = null;
                }
            }
            else if (trailing)
            {
                // Inside the interval with trailing enabled — schedule for later.
                if (state.PendingCts != null) state.PendingCts.Cancel();
                var cts = new CancellationTokenSource();
                state.PendingProceed = proceed;
                state.PendingCts = cts;
                var remainingMs = (int)(intervalMs - elapsed);
                _ = FireTrailingAsync(state, cts.Token, remainingMs);
                return state.LastResult;
            }
            else
            {
                // Inside the interval, no trailing — drop the call silently.
                return state.LastResult;
            }
        }

        var result = await proceed().ConfigureAwait(false);

        lock (state)
        {
            state.LastResult = result;
        }

        return result;
    }

    private static async Task FireTrailingAsync(ThrottleState state, CancellationToken ct, int delayMs)
    {
        try
        {
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // A newer call superseded this trailing fire.
        }

        Func<ValueTask<object?>>? pending;
        lock (state)
        {
            pending = state.PendingProceed;
            state.PendingProceed = null;
            state.PendingCts = null;
            state.LastExecutedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        if (pending != null)
        {
            try
            {
                var result = await pending().ConfigureAwait(false);
                lock (state) { state.LastResult = result; }
            }
            catch
            {
                // Trailing fire failed — swallow to avoid unobserved exception.
            }
        }
    }

    private static string BuildKey(AspectContext context)
        => string.Concat(context.ClassName, ".", context.MethodName, "(", context.FormatParameters(), ")");

    private sealed class ThrottleState
    {
        public long LastExecutedAt;
        public object? LastResult;
        public Func<ValueTask<object?>>? PendingProceed;
        public CancellationTokenSource? PendingCts;
    }
}
