using Microsoft.Extensions.Logging;

namespace ZibStack.NET.Log;

/// <summary>
/// Extension methods for <see cref="ILogger"/> that accept interpolated strings
/// while preserving structured logging.
/// </summary>
/// <example>
/// <code>
/// _logger.LogInformationEx($"User {userId} bought {product} for {total:C}");
/// // Structured template: "User {userId} bought {product} for {total:C}"
/// // Serilog/Seq capture: userId=42, product="Widget", total=29.97
/// </code>
/// </example>
public static class LoggerInterpolatedExtensions
{
    public static void LogTraceEx(this ILogger logger, ref ZibLogInterpolatedStringHandler handler)
    {
        if (logger.IsEnabled(LogLevel.Trace))
            logger.Log(LogLevel.Trace, handler.GetTemplate(), handler.GetArgs());
    }

    public static void LogDebugEx(this ILogger logger, ref ZibLogInterpolatedStringHandler handler)
    {
        if (logger.IsEnabled(LogLevel.Debug))
            logger.Log(LogLevel.Debug, handler.GetTemplate(), handler.GetArgs());
    }

    public static void LogInformationEx(this ILogger logger, ref ZibLogInterpolatedStringHandler handler)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.Log(LogLevel.Information, handler.GetTemplate(), handler.GetArgs());
    }

    public static void LogWarningEx(this ILogger logger, ref ZibLogInterpolatedStringHandler handler)
    {
        if (logger.IsEnabled(LogLevel.Warning))
            logger.Log(LogLevel.Warning, handler.GetTemplate(), handler.GetArgs());
    }

    public static void LogErrorEx(this ILogger logger, ref ZibLogInterpolatedStringHandler handler)
    {
        if (logger.IsEnabled(LogLevel.Error))
            logger.Log(LogLevel.Error, handler.GetTemplate(), handler.GetArgs());
    }

    public static void LogErrorEx(this ILogger logger, Exception exception, ref ZibLogInterpolatedStringHandler handler)
    {
        if (logger.IsEnabled(LogLevel.Error))
            logger.Log(LogLevel.Error, exception, handler.GetTemplate(), handler.GetArgs());
    }

    public static void LogCriticalEx(this ILogger logger, ref ZibLogInterpolatedStringHandler handler)
    {
        if (logger.IsEnabled(LogLevel.Critical))
            logger.Log(LogLevel.Critical, handler.GetTemplate(), handler.GetArgs());
    }

    public static void LogCriticalEx(this ILogger logger, Exception exception, ref ZibLogInterpolatedStringHandler handler)
    {
        if (logger.IsEnabled(LogLevel.Critical))
            logger.Log(LogLevel.Critical, exception, handler.GetTemplate(), handler.GetArgs());
    }
}
