using Microsoft.Extensions.Logging;
using ZibStack.NET.Log;

namespace ZibStack.NET.Log.Benchmarks;

// ─── Service WITH ZibStack.Log interceptors (logger from DI) ───
public class ZibLogService
{
    [Log]
    public int Add(int a, int b) => a + b;

    [Log(MeasureElapsed = false)]
    public int AddNoStopwatch(int a, int b) => a + b;

    [Log]
    public async Task<int> AddAsync(int a, int b)
    {
        await Task.CompletedTask;
        return a + b;
    }

    [Log]
    public string Concat(string a, string b, string c) => a + b + c;
}

// ─── Service with MANUAL logging (baseline) ───
public class ManualLogService
{
    private readonly ILogger<ManualLogService> _logger;

    public ManualLogService(ILogger<ManualLogService> logger) => _logger = logger;

    public int Add(int a, int b)
    {
        _logger.LogInformation("Entering ManualLogService.Add(a: {a}, b: {b})", a, b);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = a + b;
        sw.Stop();
        _logger.LogInformation("Exited ManualLogService.Add in {ElapsedMs}ms -> {Result}", sw.ElapsedMilliseconds, result);
        return result;
    }
}

// ─── Service with LoggerMessage.Define (hand-optimized baseline) ───
public class OptimizedManualLogService
{
    private static readonly Action<ILogger, int, int, Exception?> _logAddEntry =
        LoggerMessage.Define<int, int>(
            LogLevel.Information,
            new EventId(1, "Add_Entry"),
            "Entering OptimizedManualLogService.Add(a: {a}, b: {b})");

    private static readonly Action<ILogger, long, string?, Exception?> _logAddExit =
        LoggerMessage.Define<long, string?>(
            LogLevel.Information,
            new EventId(2, "Add_Exit"),
            "Exited OptimizedManualLogService.Add in {ElapsedMs}ms -> {Result}");

    private readonly ILogger<OptimizedManualLogService> _logger;

    public OptimizedManualLogService(ILogger<OptimizedManualLogService> logger) => _logger = logger;

    public int Add(int a, int b)
    {
        _logAddEntry(_logger, a, b, null);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = a + b;
        sw.Stop();
        _logAddExit(_logger, sw.ElapsedMilliseconds, result.ToString(), null);
        return result;
    }
}

// ─── Bare service (no logging at all) ───
public class NoLogService
{
    public int Add(int a, int b) => a + b;
}
