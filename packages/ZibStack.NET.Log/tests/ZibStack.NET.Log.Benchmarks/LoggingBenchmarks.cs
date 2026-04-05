using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZibStack.NET.Log.Benchmarks;

[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[HideColumns(Column.Error, Column.StdDev, Column.RatioSD)]
public class LoggingBenchmarks
{
    private NoLogService _noLog = null!;
    private ZibLogService _zibLog = null!;
    private ManualLogService _manual = null!;
    private OptimizedManualLogService _optimized = null!;

    // NullLogger — logging is enabled but goes nowhere (measures pure overhead)
    private ZibLogService _smartLogNull = null!;
    private ManualLogService _manualNull = null!;
    private OptimizedManualLogService _optimizedNull = null!;

    [GlobalSetup]
    public void Setup()
    {
        // With real logger factory (MinLevel = Information)
        var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(NullLoggerProvider.Instance));

        _noLog = new NoLogService();
        _zibLog = new ZibLogService(factory.CreateLogger<ZibLogService>());
        _manual = new ManualLogService(factory.CreateLogger<ManualLogService>());
        _optimized = new OptimizedManualLogService(factory.CreateLogger<OptimizedManualLogService>());

        // With NullLogger (IsEnabled returns false — measures overhead when logging is off)
        _smartLogNull = new ZibLogService(NullLogger<ZibLogService>.Instance);
        _manualNull = new ManualLogService(NullLogger<ManualLogService>.Instance);
        _optimizedNull = new OptimizedManualLogService(NullLogger<OptimizedManualLogService>.Instance);
    }

    // ═══════════════════════════════════════════
    // Logging ENABLED (NullLoggerProvider — enabled but discards output)
    // ═══════════════════════════════════════════

    [Benchmark(Baseline = true, Description = "No logging (baseline)")]
    public int NoLogging() => _noLog.Add(1, 2);

    [Benchmark(Description = "ZibStack.Log [Log]")]
    public int ZibLog_Enabled() => _zibLog.Add(1, 2);

    [Benchmark(Description = "ZibStack.Log [Log] no stopwatch")]
    public int ZibLog_NoStopwatch() => _zibLog.AddNoStopwatch(1, 2);

    [Benchmark(Description = "Manual ILogger.Log()")]
    public int Manual_Enabled() => _manual.Add(1, 2);

    [Benchmark(Description = "Manual LoggerMessage.Define")]
    public int Optimized_Enabled() => _optimized.Add(1, 2);

    // ═══════════════════════════════════════════
    // Logging DISABLED (NullLogger — IsEnabled returns false)
    // ═══════════════════════════════════════════

    [Benchmark(Description = "ZibStack.Log [Log] (level OFF)")]
    public int ZibLog_Disabled() => _smartLogNull.Add(1, 2);

    [Benchmark(Description = "Manual ILogger.Log() (level OFF)")]
    public int Manual_Disabled() => _manualNull.Add(1, 2);

    [Benchmark(Description = "Manual LoggerMessage.Define (level OFF)")]
    public int Optimized_Disabled() => _optimizedNull.Add(1, 2);
}
