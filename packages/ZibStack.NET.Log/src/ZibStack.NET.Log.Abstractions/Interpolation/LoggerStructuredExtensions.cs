using Microsoft.Extensions.Logging;

namespace ZibStack.NET.Log;

/// <summary>
/// Extension methods for <see cref="ILogger"/> that automatically convert interpolated strings
/// into structured log messages. Each method uses a per-level handler with <c>shouldAppend</c> —
/// when the log level is disabled, the interpolated string is never evaluated (zero cost).
/// C# 10+ automatically prefers these handler overloads for <c>$"..."</c> arguments.
/// </summary>
public static class LoggerStructuredExtensions
{
    // ── Trace ────────────────────────────────────────────────────────────

    public static void LogTrace(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogTraceHandler handler)
    {
        if (handler.IsEnabled)
            logger.Log(LogLevel.Trace, handler.GetTemplate(), handler.GetArgs());
    }

    public static void LogTrace(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogTraceHandler handler)
    {
        if (handler.IsEnabled)
            logger.Log(LogLevel.Trace, exception, handler.GetTemplate(), handler.GetArgs());
    }

    // ── Debug ────────────────────────────────────────────────────────────

    public static void LogDebug(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogDebugHandler handler)
    {
        if (handler.IsEnabled)
            logger.Log(LogLevel.Debug, handler.GetTemplate(), handler.GetArgs());
    }

    public static void LogDebug(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogDebugHandler handler)
    {
        if (handler.IsEnabled)
            logger.Log(LogLevel.Debug, exception, handler.GetTemplate(), handler.GetArgs());
    }

    // ── Information ──────────────────────────────────────────────────────

    public static void LogInformation(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogInformationHandler handler)
    {
        if (handler.IsEnabled)
            logger.Log(LogLevel.Information, handler.GetTemplate(), handler.GetArgs());
    }

    public static void LogInformation(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogInformationHandler handler)
    {
        if (handler.IsEnabled)
            logger.Log(LogLevel.Information, exception, handler.GetTemplate(), handler.GetArgs());
    }

    // ── Warning ──────────────────────────────────────────────────────────

    public static void LogWarning(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogWarningHandler handler)
    {
        if (handler.IsEnabled)
            logger.Log(LogLevel.Warning, handler.GetTemplate(), handler.GetArgs());
    }

    public static void LogWarning(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogWarningHandler handler)
    {
        if (handler.IsEnabled)
            logger.Log(LogLevel.Warning, exception, handler.GetTemplate(), handler.GetArgs());
    }

    // ── Error ────────────────────────────────────────────────────────────

    public static void LogError(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogErrorHandler handler)
    {
        if (handler.IsEnabled)
            logger.Log(LogLevel.Error, handler.GetTemplate(), handler.GetArgs());
    }

    public static void LogError(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogErrorHandler handler)
    {
        if (handler.IsEnabled)
            logger.Log(LogLevel.Error, exception, handler.GetTemplate(), handler.GetArgs());
    }

    // ── Critical ─────────────────────────────────────────────────────────

    public static void LogCritical(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogCriticalHandler handler)
    {
        if (handler.IsEnabled)
            logger.Log(LogLevel.Critical, handler.GetTemplate(), handler.GetArgs());
    }

    public static void LogCritical(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogCriticalHandler handler)
    {
        if (handler.IsEnabled)
            logger.Log(LogLevel.Critical, exception, handler.GetTemplate(), handler.GetArgs());
    }
}
