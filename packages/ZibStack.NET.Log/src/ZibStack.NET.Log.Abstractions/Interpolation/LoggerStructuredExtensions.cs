using Microsoft.Extensions.Logging;

namespace ZibStack.NET.Log;

/// <summary>
/// Extension methods for <see cref="ILogger"/> that route interpolated strings to the
/// ZibStack structured logging pipeline. C# 11 overload resolution prefers these over
/// Microsoft's <c>LogXxx(string, params object[])</c> when the argument is <c>$"..."</c>.
///
/// <para>
/// These method bodies throw at runtime — they exist only so the compiler has a target
/// for overload resolution. The ZibStack.NET.Log source generator replaces every call site
/// with a <c>[InterceptsLocation]</c> interceptor that reads the handler's typed slots and
/// dispatches through a cached <c>LoggerMessage.Define</c> delegate. If you see the
/// exception below, it means the generator is not installed — add
/// <c>dotnet add package ZibStack.NET.Log</c> (not just the Abstractions package).
/// </para>
/// </summary>
public static class LoggerStructuredExtensions
{
    private static void ThrowMissingGenerator() =>
        throw new InvalidOperationException(
            "ZibStack.NET.Log source generator is not installed. " +
            "The interpolated-string handler captured the arguments but no [InterceptsLocation] " +
            "interceptor was emitted to dispatch them. Install the full ZibStack.NET.Log package " +
            "(not just ZibStack.NET.Log.Abstractions) to enable structured interpolated logging.");

    // ── Trace ────────────────────────────────────────────────────────────

    public static void LogTrace(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogTraceHandler handler)
    {
        if (handler.IsEnabled) ThrowMissingGenerator();
    }

    public static void LogTrace(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogTraceHandler handler)
    {
        if (handler.IsEnabled) ThrowMissingGenerator();
    }

    // ── Debug ────────────────────────────────────────────────────────────

    public static void LogDebug(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogDebugHandler handler)
    {
        if (handler.IsEnabled) ThrowMissingGenerator();
    }

    public static void LogDebug(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogDebugHandler handler)
    {
        if (handler.IsEnabled) ThrowMissingGenerator();
    }

    // ── Information ──────────────────────────────────────────────────────

    public static void LogInformation(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogInformationHandler handler)
    {
        if (handler.IsEnabled) ThrowMissingGenerator();
    }

    public static void LogInformation(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogInformationHandler handler)
    {
        if (handler.IsEnabled) ThrowMissingGenerator();
    }

    // ── Warning ──────────────────────────────────────────────────────────

    public static void LogWarning(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogWarningHandler handler)
    {
        if (handler.IsEnabled) ThrowMissingGenerator();
    }

    public static void LogWarning(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogWarningHandler handler)
    {
        if (handler.IsEnabled) ThrowMissingGenerator();
    }

    // ── Error ────────────────────────────────────────────────────────────

    public static void LogError(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogErrorHandler handler)
    {
        if (handler.IsEnabled) ThrowMissingGenerator();
    }

    public static void LogError(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogErrorHandler handler)
    {
        if (handler.IsEnabled) ThrowMissingGenerator();
    }

    // ── Critical ─────────────────────────────────────────────────────────

    public static void LogCritical(
        this ILogger logger,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogCriticalHandler handler)
    {
        if (handler.IsEnabled) ThrowMissingGenerator();
    }

    public static void LogCritical(
        this ILogger logger,
        Exception exception,
        [System.Runtime.CompilerServices.InterpolatedStringHandlerArgument("logger")]
        ref ZibLogCriticalHandler handler)
    {
        if (handler.IsEnabled) ThrowMissingGenerator();
    }
}
