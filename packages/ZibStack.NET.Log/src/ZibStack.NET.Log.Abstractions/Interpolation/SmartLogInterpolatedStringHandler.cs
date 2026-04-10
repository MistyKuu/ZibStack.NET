using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace ZibStack.NET.Log;

// ═══════════════════════════════════════════════════════════════════
// Core logic shared by all per-level handlers.
// Uses ArrayPool<char> instead of StringBuilder and caches templates.
// ═══════════════════════════════════════════════════════════════════

internal struct ZibLogHandlerCore
{
    private char[]? _chars;
    private int _pos;
    private int _hash;
    private object?[]? _args;
    private int _argIndex;

    private static readonly ConcurrentDictionary<int, string> s_cache = new();

    internal bool IsEnabled
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _chars is not null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Init(int literalLength, int formattedCount)
    {
        _chars = ArrayPool<char>.Shared.Rent(literalLength + formattedCount * 20);
        _pos = 0;
        _hash = formattedCount; // seed with arity so "a{x}" != "{x}a"
        _args = new object?[formattedCount];
        _argIndex = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AppendLiteral(string s)
    {
        if (_chars is null) return;
        _hash = HashCode.Combine(_hash, RuntimeHelpers.GetHashCode(s));

        // Fast path: no braces to escape (vast majority of literals)
        if (s.AsSpan().IndexOfAny('{', '}') < 0)
        {
            EnsureCapacity(s.Length);
            s.AsSpan().CopyTo(_chars.AsSpan(_pos));
            _pos += s.Length;
            return;
        }

        // Slow path: escape { → {{ and } → }}
        foreach (var c in s)
        {
            if (c is '{' or '}')
            {
                EnsureCapacity(2);
                _chars[_pos++] = c;
                _chars[_pos++] = c;
            }
            else
            {
                EnsureCapacity(1);
                _chars[_pos++] = c;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AppendFormatted<T>(T value, string name)
    {
        if (_chars is null) return;
        _hash = HashCode.Combine(_hash, RuntimeHelpers.GetHashCode(name));
        var sanitized = SanitizeName(name);
        WritePlaceholder(sanitized, null);
        _args![_argIndex++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AppendFormatted<T>(T value, string format, string name)
    {
        if (_chars is null) return;
        _hash = HashCode.Combine(_hash, RuntimeHelpers.GetHashCode(name));
        var sanitized = SanitizeName(name);
        WritePlaceholder(sanitized, format);
        _args![_argIndex++] = value;
    }

    internal string GetTemplate()
    {
        if (_chars is null) return "";

        if (s_cache.TryGetValue(_hash, out var cached))
        {
            ArrayPool<char>.Shared.Return(_chars);
            _chars = null;
            return cached;
        }

        var template = new string(_chars, 0, _pos);
        ArrayPool<char>.Shared.Return(_chars);
        _chars = null;

        // Cap cache to avoid unbounded growth (e.g. dynamic templates)
        if (s_cache.Count < 2048)
            s_cache.TryAdd(_hash, template);

        return template;
    }

    internal object?[] GetArgs() => _args ?? Array.Empty<object?>();

    private void WritePlaceholder(string name, string? format)
    {
        var needed = name.Length + 2 + (format?.Length ?? 0) + (format is null ? 0 : 1);
        EnsureCapacity(needed);
        _chars![_pos++] = '{';
        name.AsSpan().CopyTo(_chars.AsSpan(_pos));
        _pos += name.Length;
        if (format is not null)
        {
            _chars[_pos++] = ':';
            format.AsSpan().CopyTo(_chars.AsSpan(_pos));
            _pos += format.Length;
        }
        _chars[_pos++] = '}';
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int additional)
    {
        if (_pos + additional <= _chars!.Length) return;
        Grow(additional);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int additional)
    {
        var newSize = Math.Max(_chars!.Length * 2, _pos + additional);
        var newChars = ArrayPool<char>.Shared.Rent(newSize);
        _chars.AsSpan(0, _pos).CopyTo(newChars);
        ArrayPool<char>.Shared.Return(_chars);
        _chars = newChars;
    }

    /// <summary>
    /// Sanitizes CallerArgumentExpression to a valid message template property name.
    /// "user.Name" → "userName", "items[0]" → "items0", "GetId()" → "GetId"
    /// </summary>
    internal static string SanitizeName(string expression)
    {
        if (string.IsNullOrEmpty(expression))
            return "_";

        // Fast path: simple identifier (no dots, brackets, etc.)
        bool simple = true;
        for (int i = 0; i < expression.Length; i++)
        {
            char c = expression[i];
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                simple = false;
                break;
            }
        }
        if (simple) return expression;

        // Slow path: sanitize complex expression
        Span<char> buf = expression.Length <= 128
            ? stackalloc char[expression.Length]
            : new char[expression.Length];
        int pos = 0;
        bool capitalizeNext = false;

        for (int i = 0; i < expression.Length; i++)
        {
            char c = expression[i];
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                buf[pos++] = capitalizeNext ? char.ToUpperInvariant(c) : c;
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = pos > 0;
            }
        }

        return pos > 0 ? new string(buf[..pos]) : "_";
    }
}

// ═══════════════════════════════════════════════════════════════════
// Per-level handlers — each hardcodes its LogLevel in the constructor
// so shouldAppend can check IsEnabled for the exact level.
// Zero cost when the level is disabled.
// ═══════════════════════════════════════════════════════════════════

/// <summary>Handler for <see cref="LogLevel.Trace"/> — zero cost when Trace is disabled.</summary>
[InterpolatedStringHandler]
public ref struct ZibLogTraceHandler
{
    private ZibLogHandlerCore _core;

    public ZibLogTraceHandler(int literalLength, int formattedCount, ILogger logger, out bool shouldAppend)
    {
        shouldAppend = logger.IsEnabled(LogLevel.Trace);
        if (shouldAppend) _core.Init(literalLength, formattedCount);
    }

    public void AppendLiteral(string s) => _core.AppendLiteral(s);
    public void AppendFormatted<T>(T value, [CallerArgumentExpression(nameof(value))] string name = "") => _core.AppendFormatted(value, name);
    public void AppendFormatted<T>(T value, string format, [CallerArgumentExpression(nameof(value))] string name = "") => _core.AppendFormatted(value, format, name);

    internal bool IsEnabled => _core.IsEnabled;
    internal string GetTemplate() => _core.GetTemplate();
    internal object?[] GetArgs() => _core.GetArgs();
}

/// <summary>Handler for <see cref="LogLevel.Debug"/> — zero cost when Debug is disabled.</summary>
[InterpolatedStringHandler]
public ref struct ZibLogDebugHandler
{
    private ZibLogHandlerCore _core;

    public ZibLogDebugHandler(int literalLength, int formattedCount, ILogger logger, out bool shouldAppend)
    {
        shouldAppend = logger.IsEnabled(LogLevel.Debug);
        if (shouldAppend) _core.Init(literalLength, formattedCount);
    }

    public void AppendLiteral(string s) => _core.AppendLiteral(s);
    public void AppendFormatted<T>(T value, [CallerArgumentExpression(nameof(value))] string name = "") => _core.AppendFormatted(value, name);
    public void AppendFormatted<T>(T value, string format, [CallerArgumentExpression(nameof(value))] string name = "") => _core.AppendFormatted(value, format, name);

    internal bool IsEnabled => _core.IsEnabled;
    internal string GetTemplate() => _core.GetTemplate();
    internal object?[] GetArgs() => _core.GetArgs();
}

/// <summary>Handler for <see cref="LogLevel.Information"/> — zero cost when Information is disabled.</summary>
[InterpolatedStringHandler]
public ref struct ZibLogInformationHandler
{
    private ZibLogHandlerCore _core;

    public ZibLogInformationHandler(int literalLength, int formattedCount, ILogger logger, out bool shouldAppend)
    {
        shouldAppend = logger.IsEnabled(LogLevel.Information);
        if (shouldAppend) _core.Init(literalLength, formattedCount);
    }

    public void AppendLiteral(string s) => _core.AppendLiteral(s);
    public void AppendFormatted<T>(T value, [CallerArgumentExpression(nameof(value))] string name = "") => _core.AppendFormatted(value, name);
    public void AppendFormatted<T>(T value, string format, [CallerArgumentExpression(nameof(value))] string name = "") => _core.AppendFormatted(value, format, name);

    internal bool IsEnabled => _core.IsEnabled;
    internal string GetTemplate() => _core.GetTemplate();
    internal object?[] GetArgs() => _core.GetArgs();
}

/// <summary>Handler for <see cref="LogLevel.Warning"/> — zero cost when Warning is disabled.</summary>
[InterpolatedStringHandler]
public ref struct ZibLogWarningHandler
{
    private ZibLogHandlerCore _core;

    public ZibLogWarningHandler(int literalLength, int formattedCount, ILogger logger, out bool shouldAppend)
    {
        shouldAppend = logger.IsEnabled(LogLevel.Warning);
        if (shouldAppend) _core.Init(literalLength, formattedCount);
    }

    public void AppendLiteral(string s) => _core.AppendLiteral(s);
    public void AppendFormatted<T>(T value, [CallerArgumentExpression(nameof(value))] string name = "") => _core.AppendFormatted(value, name);
    public void AppendFormatted<T>(T value, string format, [CallerArgumentExpression(nameof(value))] string name = "") => _core.AppendFormatted(value, format, name);

    internal bool IsEnabled => _core.IsEnabled;
    internal string GetTemplate() => _core.GetTemplate();
    internal object?[] GetArgs() => _core.GetArgs();
}

/// <summary>Handler for <see cref="LogLevel.Error"/> — zero cost when Error is disabled.</summary>
[InterpolatedStringHandler]
public ref struct ZibLogErrorHandler
{
    private ZibLogHandlerCore _core;

    public ZibLogErrorHandler(int literalLength, int formattedCount, ILogger logger, out bool shouldAppend)
    {
        shouldAppend = logger.IsEnabled(LogLevel.Error);
        if (shouldAppend) _core.Init(literalLength, formattedCount);
    }

    public void AppendLiteral(string s) => _core.AppendLiteral(s);
    public void AppendFormatted<T>(T value, [CallerArgumentExpression(nameof(value))] string name = "") => _core.AppendFormatted(value, name);
    public void AppendFormatted<T>(T value, string format, [CallerArgumentExpression(nameof(value))] string name = "") => _core.AppendFormatted(value, format, name);

    internal bool IsEnabled => _core.IsEnabled;
    internal string GetTemplate() => _core.GetTemplate();
    internal object?[] GetArgs() => _core.GetArgs();
}

/// <summary>Handler for <see cref="LogLevel.Critical"/> — zero cost when Critical is disabled.</summary>
[InterpolatedStringHandler]
public ref struct ZibLogCriticalHandler
{
    private ZibLogHandlerCore _core;

    public ZibLogCriticalHandler(int literalLength, int formattedCount, ILogger logger, out bool shouldAppend)
    {
        shouldAppend = logger.IsEnabled(LogLevel.Critical);
        if (shouldAppend) _core.Init(literalLength, formattedCount);
    }

    public void AppendLiteral(string s) => _core.AppendLiteral(s);
    public void AppendFormatted<T>(T value, [CallerArgumentExpression(nameof(value))] string name = "") => _core.AppendFormatted(value, name);
    public void AppendFormatted<T>(T value, string format, [CallerArgumentExpression(nameof(value))] string name = "") => _core.AppendFormatted(value, format, name);

    internal bool IsEnabled => _core.IsEnabled;
    internal string GetTemplate() => _core.GetTemplate();
    internal object?[] GetArgs() => _core.GetArgs();
}

// ═══════════════════════════════════════════════════════════════════
// Legacy handler kept for ZibExceptionInterpolatedStringHandler compat.
// Not used by the new LogXxx extension methods.
// ═══════════════════════════════════════════════════════════════════

/// <summary>
/// Interpolated string handler that captures template and arguments separately.
/// Prefer using the standard <c>LogXxx($"...")</c> methods which use per-level handlers.
/// </summary>
[InterpolatedStringHandler]
public ref struct ZibLogInterpolatedStringHandler
{
    private ZibLogHandlerCore _core;

    public ZibLogInterpolatedStringHandler(int literalLength, int formattedCount)
    {
        _core.Init(literalLength, formattedCount);
    }

    public void AppendLiteral(string s) => _core.AppendLiteral(s);
    public void AppendFormatted<T>(T value, [CallerArgumentExpression(nameof(value))] string name = "") => _core.AppendFormatted(value, name);
    public void AppendFormatted<T>(T value, string format, [CallerArgumentExpression(nameof(value))] string name = "") => _core.AppendFormatted(value, format, name);

    internal string GetTemplate() => _core.GetTemplate();
    internal object?[] GetArgs() => _core.GetArgs();
}
