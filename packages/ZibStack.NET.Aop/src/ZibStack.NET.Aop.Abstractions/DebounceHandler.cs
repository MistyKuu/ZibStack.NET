using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in handler for <see cref="DebounceAttribute"/>. Delays execution until
/// a quiet period elapses — each new call resets the timer. Only the final call
/// within the delay window actually executes.
///
/// <para>Registered automatically by <c>AddAop()</c> as a singleton.</para>
/// </summary>
public sealed class DebounceHandler : IAsyncAroundAspectHandler
{
    private readonly ConcurrentDictionary<string, DebounceEntry> _pending =
        new ConcurrentDictionary<string, DebounceEntry>();

    /// <inheritdoc />
    public async ValueTask<object?> AroundAsync(AspectContext context, Func<ValueTask<object?>> proceed)
    {
        var delayMs = 300;
        if (context.Properties.TryGetValue("DelayMs", out var d) && d is int v) delayMs = v;

        var key = BuildKey(context);

        // Cancel any previous pending call for this key.
        var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new DebounceEntry(cts, tcs);

        _pending.AddOrUpdate(key, entry, (_, old) =>
        {
            old.Cts.Cancel();
            old.Tcs.TrySetCanceled();
            return entry;
        });

        try
        {
            await Task.Delay(delayMs, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A newer call superseded this one — wait for its result.
            DebounceEntry current;
            if (_pending.TryGetValue(key, out current) && current.Tcs != tcs)
                return await current.Tcs.Task.ConfigureAwait(false);
            throw;
        }

        // We survived the delay — we are the final call. Execute.
        try
        {
            var result = await proceed().ConfigureAwait(false);
            tcs.TrySetResult(result);
            return result;
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
            throw;
        }
        finally
        {
            DebounceEntry removed;
            _pending.TryRemove(key, out removed);
            cts.Dispose();
        }
    }

    private static string BuildKey(AspectContext context)
        => string.Concat(context.ClassName, ".", context.MethodName, "(", context.FormatParameters(), ")");

    private sealed class DebounceEntry
    {
        public DebounceEntry(CancellationTokenSource cts, TaskCompletionSource<object> tcs)
        {
            Cts = cts;
            Tcs = tcs;
        }

        public CancellationTokenSource Cts { get; }
        public TaskCompletionSource<object> Tcs { get; }
    }
}
