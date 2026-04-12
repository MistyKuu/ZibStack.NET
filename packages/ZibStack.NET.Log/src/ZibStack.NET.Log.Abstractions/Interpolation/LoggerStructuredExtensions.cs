using System.Text;
using Microsoft.Extensions.Logging;

namespace ZibStack.NET.Log;

/// <summary>
/// Extension methods for <see cref="ILogger"/> that automatically convert interpolated strings
/// into structured log messages with zero boxing for primitive types.
/// <para>
/// These methods rely on the <c>ZibStack.NET.Log</c> source generator to emit interceptors
/// that read typed slots from the handler and dispatch via <c>LoggerMessage.Define&lt;T&gt;</c>.
/// Without the generator installed, these methods fall back to the standard
/// <see cref="LoggerExtensions.Log(ILogger, LogLevel, string?, object?[])"/> path (with boxing).
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
        if (!handler.IsEnabled) return;
        var (template, args) = BuildFallback(
            handler.Literals, handler.Names, handler.Formats,
            handler.ArgCount, handler.ArgTypes, handler.FallbackArgs,
            handler.L0, handler.L1, handler.L2, handler.L3, handler.L4, handler.L5,
            handler.D0, handler.D1, handler.D2, handler.D3,
            handler.M0, handler.M1,
            handler.S0, handler.S1, handler.S2, handler.S3, handler.S4, handler.S5,
            handler.O0, handler.O1);
        logger.Log(LogLevel.Trace, template, args);
    }

    public static void LogTrace(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogTraceHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, args) = BuildFallback(
            handler.Literals, handler.Names, handler.Formats,
            handler.ArgCount, handler.ArgTypes, handler.FallbackArgs,
            handler.L0, handler.L1, handler.L2, handler.L3, handler.L4, handler.L5,
            handler.D0, handler.D1, handler.D2, handler.D3,
            handler.M0, handler.M1,
            handler.S0, handler.S1, handler.S2, handler.S3, handler.S4, handler.S5,
            handler.O0, handler.O1);
        logger.Log(LogLevel.Trace, exception, template, args);
    }

    // ── Debug ────────────────────────────────────────────────────────────

    public static void LogDebug(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogDebugHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, args) = BuildFallback(
            handler.Literals, handler.Names, handler.Formats,
            handler.ArgCount, handler.ArgTypes, handler.FallbackArgs,
            handler.L0, handler.L1, handler.L2, handler.L3, handler.L4, handler.L5,
            handler.D0, handler.D1, handler.D2, handler.D3,
            handler.M0, handler.M1,
            handler.S0, handler.S1, handler.S2, handler.S3, handler.S4, handler.S5,
            handler.O0, handler.O1);
        logger.Log(LogLevel.Debug, template, args);
    }

    public static void LogDebug(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogDebugHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, args) = BuildFallback(
            handler.Literals, handler.Names, handler.Formats,
            handler.ArgCount, handler.ArgTypes, handler.FallbackArgs,
            handler.L0, handler.L1, handler.L2, handler.L3, handler.L4, handler.L5,
            handler.D0, handler.D1, handler.D2, handler.D3,
            handler.M0, handler.M1,
            handler.S0, handler.S1, handler.S2, handler.S3, handler.S4, handler.S5,
            handler.O0, handler.O1);
        logger.Log(LogLevel.Debug, exception, template, args);
    }

    // ── Information ──────────────────────────────────────────────────────

    public static void LogInformation(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogInformationHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, args) = BuildFallback(
            handler.Literals, handler.Names, handler.Formats,
            handler.ArgCount, handler.ArgTypes, handler.FallbackArgs,
            handler.L0, handler.L1, handler.L2, handler.L3, handler.L4, handler.L5,
            handler.D0, handler.D1, handler.D2, handler.D3,
            handler.M0, handler.M1,
            handler.S0, handler.S1, handler.S2, handler.S3, handler.S4, handler.S5,
            handler.O0, handler.O1);
        logger.Log(LogLevel.Information, template, args);
    }

    public static void LogInformation(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogInformationHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, args) = BuildFallback(
            handler.Literals, handler.Names, handler.Formats,
            handler.ArgCount, handler.ArgTypes, handler.FallbackArgs,
            handler.L0, handler.L1, handler.L2, handler.L3, handler.L4, handler.L5,
            handler.D0, handler.D1, handler.D2, handler.D3,
            handler.M0, handler.M1,
            handler.S0, handler.S1, handler.S2, handler.S3, handler.S4, handler.S5,
            handler.O0, handler.O1);
        logger.Log(LogLevel.Information, exception, template, args);
    }

    // ── Warning ──────────────────────────────────────────────────────────

    public static void LogWarning(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogWarningHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, args) = BuildFallback(
            handler.Literals, handler.Names, handler.Formats,
            handler.ArgCount, handler.ArgTypes, handler.FallbackArgs,
            handler.L0, handler.L1, handler.L2, handler.L3, handler.L4, handler.L5,
            handler.D0, handler.D1, handler.D2, handler.D3,
            handler.M0, handler.M1,
            handler.S0, handler.S1, handler.S2, handler.S3, handler.S4, handler.S5,
            handler.O0, handler.O1);
        logger.Log(LogLevel.Warning, template, args);
    }

    public static void LogWarning(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogWarningHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, args) = BuildFallback(
            handler.Literals, handler.Names, handler.Formats,
            handler.ArgCount, handler.ArgTypes, handler.FallbackArgs,
            handler.L0, handler.L1, handler.L2, handler.L3, handler.L4, handler.L5,
            handler.D0, handler.D1, handler.D2, handler.D3,
            handler.M0, handler.M1,
            handler.S0, handler.S1, handler.S2, handler.S3, handler.S4, handler.S5,
            handler.O0, handler.O1);
        logger.Log(LogLevel.Warning, exception, template, args);
    }

    // ── Error ────────────────────────────────────────────────────────────

    public static void LogError(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogErrorHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, args) = BuildFallback(
            handler.Literals, handler.Names, handler.Formats,
            handler.ArgCount, handler.ArgTypes, handler.FallbackArgs,
            handler.L0, handler.L1, handler.L2, handler.L3, handler.L4, handler.L5,
            handler.D0, handler.D1, handler.D2, handler.D3,
            handler.M0, handler.M1,
            handler.S0, handler.S1, handler.S2, handler.S3, handler.S4, handler.S5,
            handler.O0, handler.O1);
        logger.Log(LogLevel.Error, template, args);
    }

    public static void LogError(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogErrorHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, args) = BuildFallback(
            handler.Literals, handler.Names, handler.Formats,
            handler.ArgCount, handler.ArgTypes, handler.FallbackArgs,
            handler.L0, handler.L1, handler.L2, handler.L3, handler.L4, handler.L5,
            handler.D0, handler.D1, handler.D2, handler.D3,
            handler.M0, handler.M1,
            handler.S0, handler.S1, handler.S2, handler.S3, handler.S4, handler.S5,
            handler.O0, handler.O1);
        logger.Log(LogLevel.Error, exception, template, args);
    }

    // ── Critical ─────────────────────────────────────────────────────────

    public static void LogCritical(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogCriticalHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, args) = BuildFallback(
            handler.Literals, handler.Names, handler.Formats,
            handler.ArgCount, handler.ArgTypes, handler.FallbackArgs,
            handler.L0, handler.L1, handler.L2, handler.L3, handler.L4, handler.L5,
            handler.D0, handler.D1, handler.D2, handler.D3,
            handler.M0, handler.M1,
            handler.S0, handler.S1, handler.S2, handler.S3, handler.S4, handler.S5,
            handler.O0, handler.O1);
        logger.Log(LogLevel.Critical, template, args);
    }

    public static void LogCritical(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogCriticalHandler handler)
    {
        if (!handler.IsEnabled) return;
        var (template, args) = BuildFallback(
            handler.Literals, handler.Names, handler.Formats,
            handler.ArgCount, handler.ArgTypes, handler.FallbackArgs,
            handler.L0, handler.L1, handler.L2, handler.L3, handler.L4, handler.L5,
            handler.D0, handler.D1, handler.D2, handler.D3,
            handler.M0, handler.M1,
            handler.S0, handler.S1, handler.S2, handler.S3, handler.S4, handler.S5,
            handler.O0, handler.O1);
        logger.Log(LogLevel.Critical, exception, template, args);
    }

    // ── Shared helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Builds a structured log template and an <c>object?[]</c> of arg values from the
    /// handler's metadata and typed slots.  Boxing happens here — this is the slow-path
    /// fallback used only when no source-generated interceptor is present.
    /// </summary>
    private static (string Template, object?[] Args) BuildFallback(
        string?[]? literals, string?[]? names, string?[]? formats,
        byte argCount, uint argTypes, object?[]? fallbackArgs,
        long l0, long l1, long l2, long l3, long l4, long l5,
        double d0, double d1, double d2, double d3,
        decimal m0, decimal m1,
        string? s0, string? s1, string? s2, string? s3, string? s4, string? s5,
        object? o0, object? o1)
    {
        var template = BuildTemplate(literals, names, formats, argCount);
        var args = fallbackArgs ?? BuildArgsFromSlots(
            argCount, argTypes,
            l0, l1, l2, l3, l4, l5,
            d0, d1, d2, d3,
            m0, m1,
            s0, s1, s2, s3, s4, s5,
            o0, o1);
        return (template, args);
    }

    /// <summary>
    /// Reconstructs the MEL-style message template from captured literals, names, and formats.
    /// E.g. literals=["Order ","placed for "], names=["orderId","total"], formats=[null,"C"]
    /// produces "Order {orderId} placed for {total:C}".
    /// </summary>
    private static string BuildTemplate(string?[]? literals, string?[]? names, string?[]? formats, byte argCount)
    {
        if (argCount == 0)
        {
            // No args — template is just the concatenation of literals (usually a single one).
            if (literals is null || literals.Length == 0) return string.Empty;
            return literals[0] ?? string.Empty;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < argCount; i++)
        {
            // Literal segment before this hole.
            if (literals is not null && i < literals.Length)
                sb.Append(literals[i]);

            sb.Append('{');
            sb.Append(names is not null && i < names.Length ? names[i] ?? "arg" : "arg");
            if (formats is not null && i < formats.Length && formats[i] is not null)
            {
                sb.Append(':');
                sb.Append(formats[i]);
            }
            sb.Append('}');
        }

        // Trailing literal after the last hole.
        if (literals is not null && argCount < literals.Length)
            sb.Append(literals[argCount]);

        return sb.ToString();
    }

    /// <summary>
    /// Builds an <c>object?[]</c> from the handler's typed slots, using the
    /// <paramref name="argTypes"/> bitmask to determine which slot type each positional
    /// arg was stored in.
    /// <para>
    /// ArgTypes encoding: 3 bits per arg position (LSB-first).
    /// 0 = long, 1 = double, 2 = decimal, 3 = string, 4 = object.
    /// </para>
    /// </summary>
    private static object?[] BuildArgsFromSlots(
        byte argCount, uint argTypes,
        long l0, long l1, long l2, long l3, long l4, long l5,
        double d0, double d1, double d2, double d3,
        decimal m0, decimal m1,
        string? s0, string? s1, string? s2, string? s3, string? s4, string? s5,
        object? o0, object? o1)
    {
        var args = new object?[argCount];
        byte li = 0, di = 0, mi = 0, si = 0, oi = 0;

        for (int i = 0; i < argCount; i++)
        {
            var type = (argTypes >> (i * 3)) & 7;
            switch (type)
            {
                case 0: // long
                    args[i] = li switch
                    {
                        0 => l0, 1 => l1, 2 => l2, 3 => l3, 4 => l4, 5 => l5,
                        _ => null
                    };
                    li++;
                    break;
                case 1: // double
                    args[i] = di switch
                    {
                        0 => d0, 1 => d1, 2 => d2, 3 => d3,
                        _ => null
                    };
                    di++;
                    break;
                case 2: // decimal
                    args[i] = mi switch
                    {
                        0 => m0, 1 => m1,
                        _ => null
                    };
                    mi++;
                    break;
                case 3: // string
                    args[i] = si switch
                    {
                        0 => s0, 1 => s1, 2 => s2, 3 => s3, 4 => s4, 5 => s5,
                        _ => null
                    };
                    si++;
                    break;
                case 4: // object
                    args[i] = oi switch
                    {
                        0 => o0, 1 => o1,
                        _ => null
                    };
                    oi++;
                    break;
            }
        }

        return args;
    }
}
