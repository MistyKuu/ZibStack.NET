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

// ─── Service with [Sensitive] properties on return type ───
public class SensitiveOrder
{
    public int Id { get; set; }
    public string Product { get; set; } = "";
    public decimal Total { get; set; }
    [Sensitive] public string CreditCard { get; set; } = "";
    [NoLog] public byte[]? Payload { get; set; }
}

public class ZibLogSanitizerService
{
    [Log]
    public SensitiveOrder GetOrder(int id)
    {
        return new SensitiveOrder { Id = id, Product = "Widget", Total = 29.97m, CreditCard = "4111-1111" };
    }
}

// ─── Same but without [Sensitive] properties ───
public class PlainOrder
{
    public int Id { get; set; }
    public string Product { get; set; } = "";
    public decimal Total { get; set; }
    public string CreditCard { get; set; } = "";
}

public class ZibLogPlainService
{
    [Log]
    public PlainOrder GetOrder(int id)
    {
        return new PlainOrder { Id = id, Product = "Widget", Total = 29.97m, CreditCard = "4111-1111" };
    }
}

// ─── Interpolated string logging: structured vs standard ───
public class InterpolatedLogService
{
    private readonly ILogger _logger;
    public InterpolatedLogService(ILogger logger) => _logger = logger;

    /// <summary>
    /// ZibStack structured: logger.LogInformation($"...") → handler captures template + args.
    /// </summary>
    public void LogStructured(int userId, string product, decimal total)
    {
        _logger.LogInformation($"User {userId} bought {product} for {total:C}");
    }

    /// <summary>
    /// Standard Microsoft: logger.LogInformation("template", args) — already structured.
    /// </summary>
    public void LogStandardTemplate(int userId, string product, decimal total)
    {
        _logger.LogInformation("User {userId} bought {product} for {total}", userId, product, total);
    }

}

// ─── Consuming logger provider ───
// Reads every property/arg of every log entry. Forces JIT to actually
// allocate object[] / box values — no escape analysis tricks.
// This is the "real world" baseline a Serilog/Console/etc. sink would create.
public sealed class ConsumingLoggerProvider : ILoggerProvider
{
    public static long Sink; // volatile-ish accumulator so JIT can't elide reads

    public ILogger CreateLogger(string categoryName) => new ConsumingLogger();
    public void Dispose() { }

    private sealed class ConsumingLogger : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Touch every property — forces boxing to materialize and prevents JIT from eliding.
            if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    var v = values[i].Value;
                    if (v is not null) Sink += v.GetHashCode();
                }
            }
            else if (state is not null)
            {
                Sink += state.GetHashCode();
            }
        }
    }
}

// ─── Bare service (no logging at all) ───
public class NoLogService
{
    public int Add(int a, int b) => a + b;
}
