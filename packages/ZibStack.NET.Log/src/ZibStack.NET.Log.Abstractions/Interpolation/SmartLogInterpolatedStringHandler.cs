using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace ZibStack.NET.Log;

// ═══════════════════════════════════════════════════════════════════
// Typed-slot handlers — store args in typed fields (no boxing).
// A source-generated interceptor reads the typed slots and dispatches
// via LoggerMessage.Define<T1,...> for zero-allocation logging.
//
// Without the interceptor, the handler degrades gracefully — args go
// into the fallback object[] and the standard logger.Log path is used.
// ═══════════════════════════════════════════════════════════════════

// Slot layout (per handler instance, ~180 bytes on stack):
//   L0..L5  long  — int, long, bool, byte, short, char, uint, ulong (via cast)
//   D0..D3  double — double, float
//   M0..M1  decimal
//   S0..S5  string
//   O0..O1  object — fallback for custom types (boxes)
//
// Total slots: 18, but max 6 args per call site (LoggerMessage.Define limit).
// If formattedCount > 6, falls back to object?[] + standard logger.Log path.

#pragma warning disable CS0649 // fields assigned via slot writes

/// <summary>Handler for <see cref="LogLevel.Trace"/> — typed slots, zero boxing for primitives.</summary>
[InterpolatedStringHandler]
public ref struct ZibLogTraceHandler
{
    public bool IsEnabled;
    public long L0, L1, L2, L3, L4, L5;
    public double D0, D1, D2, D3;
    public decimal M0, M1;
    public string? S0, S1, S2, S3, S4, S5;
    public object? O0, O1;
    private byte _li, _di, _mi, _si, _oi;
    public object?[]? FallbackArgs;
    private byte _fi;

    public ZibLogTraceHandler(int literalLength, int formattedCount, ILogger logger, out bool shouldAppend)
    {
        IsEnabled = logger.IsEnabled(LogLevel.Trace);
        shouldAppend = IsEnabled;
        if (IsEnabled && formattedCount > 6)
            FallbackArgs = new object?[formattedCount];
    }

    public void AppendLiteral(string s) { }

    public void AppendFormatted(int v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(int v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(long v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(long v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(bool v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v ? 1 : 0);
    public void AppendFormatted(byte v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(short v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(char v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(uint v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(double v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(double v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(float v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(decimal v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDecimal(v);
    public void AppendFormatted(decimal v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDecimal(v);
    public void AppendFormatted(string? v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreString(v);
    public void AppendFormatted(string? v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreString(v);
    public void AppendFormatted<T>(T v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreObject(v);
    public void AppendFormatted<T>(T v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreObject(v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StoreLong(long v)
    {
        if (!IsEnabled) return;
        if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; }
        switch (_li++) { case 0: L0 = v; break; case 1: L1 = v; break; case 2: L2 = v; break; case 3: L3 = v; break; case 4: L4 = v; break; case 5: L5 = v; break; }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StoreDouble(double v)
    {
        if (!IsEnabled) return;
        if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; }
        switch (_di++) { case 0: D0 = v; break; case 1: D1 = v; break; case 2: D2 = v; break; case 3: D3 = v; break; }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StoreDecimal(decimal v)
    {
        if (!IsEnabled) return;
        if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; }
        switch (_mi++) { case 0: M0 = v; break; case 1: M1 = v; break; }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StoreString(string? v)
    {
        if (!IsEnabled) return;
        if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; }
        switch (_si++) { case 0: S0 = v; break; case 1: S1 = v; break; case 2: S2 = v; break; case 3: S3 = v; break; case 4: S4 = v; break; case 5: S5 = v; break; }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StoreObject<T>(T v)
    {
        if (!IsEnabled) return;
        if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; }
        switch (_oi++) { case 0: O0 = v; break; case 1: O1 = v; break; }
    }
}

/// <summary>Handler for <see cref="LogLevel.Debug"/> — typed slots, zero boxing for primitives.</summary>
[InterpolatedStringHandler]
public ref struct ZibLogDebugHandler
{
    public bool IsEnabled;
    public long L0, L1, L2, L3, L4, L5;
    public double D0, D1, D2, D3;
    public decimal M0, M1;
    public string? S0, S1, S2, S3, S4, S5;
    public object? O0, O1;
    private byte _li, _di, _mi, _si, _oi;
    public object?[]? FallbackArgs;
    private byte _fi;

    public ZibLogDebugHandler(int literalLength, int formattedCount, ILogger logger, out bool shouldAppend)
    {
        IsEnabled = logger.IsEnabled(LogLevel.Debug);
        shouldAppend = IsEnabled;
        if (IsEnabled && formattedCount > 6)
            FallbackArgs = new object?[formattedCount];
    }

    public void AppendLiteral(string s) { }
    public void AppendFormatted(int v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(int v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(long v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(long v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(bool v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v ? 1 : 0);
    public void AppendFormatted(byte v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(short v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(char v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(uint v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(double v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(double v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(float v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(decimal v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDecimal(v);
    public void AppendFormatted(decimal v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDecimal(v);
    public void AppendFormatted(string? v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreString(v);
    public void AppendFormatted(string? v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreString(v);
    public void AppendFormatted<T>(T v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreObject(v);
    public void AppendFormatted<T>(T v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreObject(v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreLong(long v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_li++) { case 0: L0 = v; break; case 1: L1 = v; break; case 2: L2 = v; break; case 3: L3 = v; break; case 4: L4 = v; break; case 5: L5 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreDouble(double v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_di++) { case 0: D0 = v; break; case 1: D1 = v; break; case 2: D2 = v; break; case 3: D3 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreDecimal(decimal v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_mi++) { case 0: M0 = v; break; case 1: M1 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreString(string? v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_si++) { case 0: S0 = v; break; case 1: S1 = v; break; case 2: S2 = v; break; case 3: S3 = v; break; case 4: S4 = v; break; case 5: S5 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreObject<T>(T v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_oi++) { case 0: O0 = v; break; case 1: O1 = v; break; } }
}

/// <summary>Handler for <see cref="LogLevel.Information"/> — typed slots, zero boxing for primitives.</summary>
[InterpolatedStringHandler]
public ref struct ZibLogInformationHandler
{
    public bool IsEnabled;
    public long L0, L1, L2, L3, L4, L5;
    public double D0, D1, D2, D3;
    public decimal M0, M1;
    public string? S0, S1, S2, S3, S4, S5;
    public object? O0, O1;
    private byte _li, _di, _mi, _si, _oi;
    public object?[]? FallbackArgs;
    private byte _fi;

    public ZibLogInformationHandler(int literalLength, int formattedCount, ILogger logger, out bool shouldAppend)
    {
        IsEnabled = logger.IsEnabled(LogLevel.Information);
        shouldAppend = IsEnabled;
        if (IsEnabled && formattedCount > 6)
            FallbackArgs = new object?[formattedCount];
    }

    public void AppendLiteral(string s) { }
    public void AppendFormatted(int v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(int v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(long v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(long v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(bool v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v ? 1 : 0);
    public void AppendFormatted(byte v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(short v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(char v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(uint v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(double v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(double v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(float v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(decimal v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDecimal(v);
    public void AppendFormatted(decimal v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDecimal(v);
    public void AppendFormatted(string? v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreString(v);
    public void AppendFormatted(string? v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreString(v);
    public void AppendFormatted<T>(T v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreObject(v);
    public void AppendFormatted<T>(T v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreObject(v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreLong(long v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_li++) { case 0: L0 = v; break; case 1: L1 = v; break; case 2: L2 = v; break; case 3: L3 = v; break; case 4: L4 = v; break; case 5: L5 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreDouble(double v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_di++) { case 0: D0 = v; break; case 1: D1 = v; break; case 2: D2 = v; break; case 3: D3 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreDecimal(decimal v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_mi++) { case 0: M0 = v; break; case 1: M1 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreString(string? v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_si++) { case 0: S0 = v; break; case 1: S1 = v; break; case 2: S2 = v; break; case 3: S3 = v; break; case 4: S4 = v; break; case 5: S5 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreObject<T>(T v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_oi++) { case 0: O0 = v; break; case 1: O1 = v; break; } }
}

/// <summary>Handler for <see cref="LogLevel.Warning"/> — typed slots, zero boxing for primitives.</summary>
[InterpolatedStringHandler]
public ref struct ZibLogWarningHandler
{
    public bool IsEnabled;
    public long L0, L1, L2, L3, L4, L5;
    public double D0, D1, D2, D3;
    public decimal M0, M1;
    public string? S0, S1, S2, S3, S4, S5;
    public object? O0, O1;
    private byte _li, _di, _mi, _si, _oi;
    public object?[]? FallbackArgs;
    private byte _fi;

    public ZibLogWarningHandler(int literalLength, int formattedCount, ILogger logger, out bool shouldAppend)
    {
        IsEnabled = logger.IsEnabled(LogLevel.Warning);
        shouldAppend = IsEnabled;
        if (IsEnabled && formattedCount > 6)
            FallbackArgs = new object?[formattedCount];
    }

    public void AppendLiteral(string s) { }
    public void AppendFormatted(int v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(int v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(long v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(long v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(bool v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v ? 1 : 0);
    public void AppendFormatted(byte v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(short v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(char v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(uint v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(double v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(double v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(float v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(decimal v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDecimal(v);
    public void AppendFormatted(decimal v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDecimal(v);
    public void AppendFormatted(string? v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreString(v);
    public void AppendFormatted(string? v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreString(v);
    public void AppendFormatted<T>(T v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreObject(v);
    public void AppendFormatted<T>(T v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreObject(v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreLong(long v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_li++) { case 0: L0 = v; break; case 1: L1 = v; break; case 2: L2 = v; break; case 3: L3 = v; break; case 4: L4 = v; break; case 5: L5 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreDouble(double v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_di++) { case 0: D0 = v; break; case 1: D1 = v; break; case 2: D2 = v; break; case 3: D3 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreDecimal(decimal v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_mi++) { case 0: M0 = v; break; case 1: M1 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreString(string? v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_si++) { case 0: S0 = v; break; case 1: S1 = v; break; case 2: S2 = v; break; case 3: S3 = v; break; case 4: S4 = v; break; case 5: S5 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreObject<T>(T v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_oi++) { case 0: O0 = v; break; case 1: O1 = v; break; } }
}

/// <summary>Handler for <see cref="LogLevel.Error"/> — typed slots, zero boxing for primitives.</summary>
[InterpolatedStringHandler]
public ref struct ZibLogErrorHandler
{
    public bool IsEnabled;
    public long L0, L1, L2, L3, L4, L5;
    public double D0, D1, D2, D3;
    public decimal M0, M1;
    public string? S0, S1, S2, S3, S4, S5;
    public object? O0, O1;
    private byte _li, _di, _mi, _si, _oi;
    public object?[]? FallbackArgs;
    private byte _fi;

    public ZibLogErrorHandler(int literalLength, int formattedCount, ILogger logger, out bool shouldAppend)
    {
        IsEnabled = logger.IsEnabled(LogLevel.Error);
        shouldAppend = IsEnabled;
        if (IsEnabled && formattedCount > 6)
            FallbackArgs = new object?[formattedCount];
    }

    public void AppendLiteral(string s) { }
    public void AppendFormatted(int v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(int v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(long v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(long v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(bool v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v ? 1 : 0);
    public void AppendFormatted(byte v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(short v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(char v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(uint v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(double v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(double v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(float v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(decimal v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDecimal(v);
    public void AppendFormatted(decimal v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDecimal(v);
    public void AppendFormatted(string? v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreString(v);
    public void AppendFormatted(string? v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreString(v);
    public void AppendFormatted<T>(T v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreObject(v);
    public void AppendFormatted<T>(T v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreObject(v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreLong(long v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_li++) { case 0: L0 = v; break; case 1: L1 = v; break; case 2: L2 = v; break; case 3: L3 = v; break; case 4: L4 = v; break; case 5: L5 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreDouble(double v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_di++) { case 0: D0 = v; break; case 1: D1 = v; break; case 2: D2 = v; break; case 3: D3 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreDecimal(decimal v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_mi++) { case 0: M0 = v; break; case 1: M1 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreString(string? v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_si++) { case 0: S0 = v; break; case 1: S1 = v; break; case 2: S2 = v; break; case 3: S3 = v; break; case 4: S4 = v; break; case 5: S5 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreObject<T>(T v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_oi++) { case 0: O0 = v; break; case 1: O1 = v; break; } }
}

/// <summary>Handler for <see cref="LogLevel.Critical"/> — typed slots, zero boxing for primitives.</summary>
[InterpolatedStringHandler]
public ref struct ZibLogCriticalHandler
{
    public bool IsEnabled;
    public long L0, L1, L2, L3, L4, L5;
    public double D0, D1, D2, D3;
    public decimal M0, M1;
    public string? S0, S1, S2, S3, S4, S5;
    public object? O0, O1;
    private byte _li, _di, _mi, _si, _oi;
    public object?[]? FallbackArgs;
    private byte _fi;

    public ZibLogCriticalHandler(int literalLength, int formattedCount, ILogger logger, out bool shouldAppend)
    {
        IsEnabled = logger.IsEnabled(LogLevel.Critical);
        shouldAppend = IsEnabled;
        if (IsEnabled && formattedCount > 6)
            FallbackArgs = new object?[formattedCount];
    }

    public void AppendLiteral(string s) { }
    public void AppendFormatted(int v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(int v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(long v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(long v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(bool v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v ? 1 : 0);
    public void AppendFormatted(byte v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(short v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(char v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(uint v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreLong(v);
    public void AppendFormatted(double v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(double v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(float v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDouble(v);
    public void AppendFormatted(decimal v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDecimal(v);
    public void AppendFormatted(decimal v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreDecimal(v);
    public void AppendFormatted(string? v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreString(v);
    public void AppendFormatted(string? v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreString(v);
    public void AppendFormatted<T>(T v, [CallerArgumentExpression(nameof(v))] string name = "") => StoreObject(v);
    public void AppendFormatted<T>(T v, string format, [CallerArgumentExpression(nameof(v))] string name = "") => StoreObject(v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreLong(long v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_li++) { case 0: L0 = v; break; case 1: L1 = v; break; case 2: L2 = v; break; case 3: L3 = v; break; case 4: L4 = v; break; case 5: L5 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreDouble(double v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_di++) { case 0: D0 = v; break; case 1: D1 = v; break; case 2: D2 = v; break; case 3: D3 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreDecimal(decimal v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_mi++) { case 0: M0 = v; break; case 1: M1 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreString(string? v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_si++) { case 0: S0 = v; break; case 1: S1 = v; break; case 2: S2 = v; break; case 3: S3 = v; break; case 4: S4 = v; break; case 5: S5 = v; break; } }
    [MethodImpl(MethodImplOptions.AggressiveInlining)] private void StoreObject<T>(T v) { if (!IsEnabled) return; if (FallbackArgs is not null) { FallbackArgs[_fi++] = v; return; } switch (_oi++) { case 0: O0 = v; break; case 1: O1 = v; break; } }
}

#pragma warning restore CS0649
