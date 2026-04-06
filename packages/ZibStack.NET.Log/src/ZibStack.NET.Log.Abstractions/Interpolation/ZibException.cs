using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace ZibStack.NET.Log;

/// <summary>
/// Exception that preserves structured logging data from interpolated strings.
/// Use like a normal exception with interpolated strings — the template and properties
/// are captured automatically for structured logging.
/// </summary>
/// <example>
/// <code>
/// throw new ZibException($"Order {orderId} not found for user {userId}");
/// // Message: "Order 123 not found for user 42"
/// // Template: "Order {orderId} not found for user {userId}"
/// // Properties: { orderId: 123, userId: 42 }
///
/// // Later when catching:
/// catch (ZibException ex)
/// {
///     ex.LogTo(logger, LogLevel.Error);
///     // Structured log: "Order {orderId} not found for user {userId}" with orderId=123, userId=42
/// }
/// </code>
/// </example>
public class ZibException : Exception
{
    /// <summary>The structured message template (e.g. "Order {orderId} not found").</summary>
    public string Template { get; }

    /// <summary>Captured property names and values from the interpolated string.</summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Properties { get; }

    public ZibException(ref ZibExceptionInterpolatedStringHandler handler)
        : base(handler.GetMessage())
    {
        Template = handler.GetTemplate();
        Properties = handler.GetProperties();
    }

    public ZibException(ref ZibExceptionInterpolatedStringHandler handler, Exception innerException)
        : base(handler.GetMessage(), innerException)
    {
        Template = handler.GetTemplate();
        Properties = handler.GetProperties();
    }

    /// <summary>
    /// Log this exception with structured data preserved.
    /// </summary>
    public void LogTo(ILogger logger, LogLevel level = LogLevel.Error)
    {
        if (!logger.IsEnabled(level)) return;

        var args = new object?[Properties.Count];
        for (int i = 0; i < Properties.Count; i++)
            args[i] = Properties[i].Value;

        logger.Log(level, this, Template, args);
    }
}

/// <summary>
/// Typed version of ZibException for domain-specific exception hierarchies.
/// </summary>
public class ZibException<TCode> : ZibException where TCode : struct, Enum
{
    /// <summary>Application-specific error code.</summary>
    public TCode Code { get; }

    public ZibException(TCode code, ref ZibExceptionInterpolatedStringHandler handler)
        : base(ref handler)
    {
        Code = code;
    }

    public ZibException(TCode code, ref ZibExceptionInterpolatedStringHandler handler, Exception innerException)
        : base(ref handler, innerException)
    {
        Code = code;
    }
}

/// <summary>
/// Extensions for logging any exception, with special handling for ZibException.
/// </summary>
public static class ZibExceptionLoggerExtensions
{
    /// <summary>
    /// Logs the exception. If it's a ZibException, preserves structured data.
    /// Otherwise falls back to standard exception logging.
    /// </summary>
    public static void LogException(this ILogger logger, Exception exception, LogLevel level = LogLevel.Error)
    {
        if (!logger.IsEnabled(level)) return;

        if (exception is ZibException zibEx)
        {
            zibEx.LogTo(logger, level);
        }
        else
        {
            logger.Log(level, exception, exception.Message);
        }
    }
}
