using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZibStack.NET.Aop;

/// <summary>
/// Built-in handler for <see cref="RetryAttribute"/>. Wraps method execution in a retry loop
/// with configurable delay and exponential backoff.
/// Implements both sync and async around handlers.
///
/// <para>
/// Registered automatically by <c>AddAop()</c> as a singleton.
/// </para>
/// </summary>
public sealed class RetryHandler : IAroundAspectHandler, IAsyncAroundAspectHandler
{
    /// <inheritdoc />
    public object? Around(AspectContext context, Func<object?> proceed)
    {
        var cfg = ReadConfig(context);

        var currentDelay = cfg.DelayMs;
        for (int attempt = 1; attempt <= cfg.MaxAttempts; attempt++)
        {
            try
            {
                return proceed();
            }
            catch (Exception ex) when (attempt < cfg.MaxAttempts && ShouldRetry(ex, cfg))
            {
                if (currentDelay > 0) Thread.Sleep(currentDelay);
                currentDelay = (int)(currentDelay * cfg.Backoff);
            }
        }

        return proceed();
    }

    /// <inheritdoc />
    public async ValueTask<object?> AroundAsync(AspectContext context, Func<ValueTask<object?>> proceed)
    {
        var cfg = ReadConfig(context);

        var currentDelay = cfg.DelayMs;
        for (int attempt = 1; attempt <= cfg.MaxAttempts; attempt++)
        {
            try
            {
                return await proceed().ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < cfg.MaxAttempts && ShouldRetry(ex, cfg))
            {
                if (currentDelay > 0) await Task.Delay(currentDelay).ConfigureAwait(false);
                currentDelay = (int)(currentDelay * cfg.Backoff);
            }
        }

        return await proceed().ConfigureAwait(false);
    }

    private static bool ShouldRetry(Exception ex, RetryConfig cfg)
    {
        if (cfg.Handle is not null)
            return MatchesAny(ex, cfg.Handle);

        if (cfg.Ignore is not null)
            return !MatchesAny(ex, cfg.Ignore);

        return true;
    }

    private static bool MatchesAny(Exception ex, Type[] types)
    {
        var exType = ex.GetType();
        foreach (var t in types)
        {
            if (t.IsAssignableFrom(exType))
                return true;
        }
        return false;
    }

    private static RetryConfig ReadConfig(AspectContext context)
    {
        var maxAttempts = 3;
        var delayMs = 0;
        var backoff = 1.0;
        Type[]? handle = null;
        Type[]? ignore = null;

        if (context.Properties.TryGetValue("MaxAttempts", out var ma) && ma is int m) maxAttempts = m;
        if (context.Properties.TryGetValue("DelayMs", out var dm) && dm is int d) delayMs = d;
        if (context.Properties.TryGetValue("BackoffMultiplier", out var bm) && bm is double b) backoff = b;
        if (context.Properties.TryGetValue("Handle", out var hf) && hf is Type[] h) handle = h;
        if (context.Properties.TryGetValue("Ignore", out var ig) && ig is Type[] i) ignore = i;

        if (maxAttempts < 1) maxAttempts = 1;

        return new RetryConfig(maxAttempts, delayMs, backoff, handle, ignore);
    }

    private sealed record RetryConfig(int MaxAttempts, int DelayMs, double Backoff, Type[]? Handle, Type[]? Ignore);
}
