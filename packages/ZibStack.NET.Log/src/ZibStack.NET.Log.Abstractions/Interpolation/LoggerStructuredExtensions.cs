using Microsoft.Extensions.Logging;

namespace ZibStack.NET.Log;

/// <summary>
/// Extension methods for <see cref="ILogger"/> that automatically convert interpolated strings
/// into structured log messages with zero boxing for primitive types.
/// <para>
/// These methods rely on the <c>ZibStack.NET.Log</c> source generator to emit interceptors
/// that read typed slots from the handler and dispatch via <c>LoggerMessage.Define&lt;T&gt;</c>.
/// Without the generator installed, these methods are no-ops.
/// </para>
/// </summary>
public static class LoggerStructuredExtensions
{
    // ── Trace ────────────────────────────────────────────────────────────

    public static void LogTrace(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogTraceHandler handler)
    {
        // No-op fallback. Replaced by generator-emitted [InterceptsLocation] interceptor.
    }

    public static void LogTrace(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogTraceHandler handler)
    {
    }

    // ── Debug ────────────────────────────────────────────────────────────

    public static void LogDebug(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogDebugHandler handler)
    {
    }

    public static void LogDebug(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogDebugHandler handler)
    {
    }

    // ── Information ──────────────────────────────────────────────────────

    public static void LogInformation(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogInformationHandler handler)
    {
    }

    public static void LogInformation(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogInformationHandler handler)
    {
    }

    // ── Warning ──────────────────────────────────────────────────────────

    public static void LogWarning(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogWarningHandler handler)
    {
    }

    public static void LogWarning(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogWarningHandler handler)
    {
    }

    // ── Error ────────────────────────────────────────────────────────────

    public static void LogError(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogErrorHandler handler)
    {
    }

    public static void LogError(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogErrorHandler handler)
    {
    }

    // ── Critical ─────────────────────────────────────────────────────────

    public static void LogCritical(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogCriticalHandler handler)
    {
    }

    public static void LogCritical(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogCriticalHandler handler)
    {
    }
}
