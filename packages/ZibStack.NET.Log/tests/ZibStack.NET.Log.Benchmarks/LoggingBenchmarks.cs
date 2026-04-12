using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Sinks.InMemory;

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

    private ZibLogSanitizerService _sanitizerService = null!;
    private ZibLogPlainService _plainService = null!;

    // Interpolated string logging benchmarks
    private InterpolatedLogService _interpolated = null!;
    private InterpolatedLogService _interpolatedNull = null!;

    // Real-world allocation measurement: consuming sink reads all properties
    // so JIT can't eliminate boxing via escape analysis.
    private InterpolatedLogService _interpolatedReal = null!;

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
        _zibLog = new ZibLogService();
        _sanitizerService = new ZibLogSanitizerService();
        _plainService = new ZibLogPlainService();
        _manual = new ManualLogService(factory.CreateLogger<ManualLogService>());
        _optimized = new OptimizedManualLogService(factory.CreateLogger<OptimizedManualLogService>());

        // Interpolated string logging (with NullLoggerProvider — JIT may elide)
        _interpolated = new InterpolatedLogService(factory.CreateLogger<InterpolatedLogService>());
        _interpolatedNull = new InterpolatedLogService(NullLogger<InterpolatedLogService>.Instance);

        // Real-world: Serilog with in-memory sink. Serilog enumerates the
        // FormattedLogValues state and materializes properties — exactly what
        // a production sink (Seq/Console/File) would do, so JIT can't elide
        // the boxing of typed args.
        var serilogLogger = new global::Serilog.LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.InMemory()
            .CreateLogger();
        var serilogFactory = LoggerFactory.Create(b => b
            .SetMinimumLevel(LogLevel.Information)
            .AddSerilog(serilogLogger));
        _interpolatedReal = new InterpolatedLogService(serilogFactory.CreateLogger<InterpolatedLogService>());

        // With NullLogger (IsEnabled returns false — measures overhead when logging is off)
        _smartLogNull = new ZibLogService();
        _manualNull = new ManualLogService(NullLogger<ManualLogService>.Instance);
        _optimizedNull = new OptimizedManualLogService(NullLogger<OptimizedManualLogService>.Instance);

        // Wire up DI for ZibLog — use a simple service provider
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<ILoggerFactory>(factory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        ZibStack.NET.Aop.AspectServiceProvider.ServiceProvider = services.BuildServiceProvider();
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

    // ═══════════════════════════════════════════
    // Complex objects: with vs without [Sensitive] property sanitization
    // ═══════════════════════════════════════════

    [Benchmark(Description = "[Log] return object (no [Sensitive])")]
    public PlainOrder Log_PlainObject() => _plainService.GetOrder(1);

    [Benchmark(Description = "[Log] return object (with [Sensitive])")]
    public SensitiveOrder Log_SanitizedObject() => _sanitizerService.GetOrder(1);

    // ═══════════════════════════════════════════
    // Interpolated string logging: structured vs standard template
    // ═══════════════════════════════════════════

    [Benchmark(Description = "LogInformation($\"...\") structured")]
    public void Interpolated_Structured() => _interpolated.LogStructured(42, "Widget", 29.97m);

    [Benchmark(Description = "LogInformation(\"template\", args)")]
    public void Interpolated_StandardTemplate() => _interpolated.LogStandardTemplate(42, "Widget", 29.97m);

    [Benchmark(Description = "LogInformation($\"...\") (level OFF)")]
    public void Interpolated_Structured_Off() => _interpolatedNull.LogStructured(42, "Widget", 29.97m);

    [Benchmark(Description = "LogInformation(\"template\") (level OFF)")]
    public void Interpolated_StandardTemplate_Off() => _interpolatedNull.LogStandardTemplate(42, "Widget", 29.97m);

    // ═══════════════════════════════════════════
    // REAL-WORLD: consuming sink reads every property
    // → JIT can't elide boxing → measures actual heap allocations
    // ═══════════════════════════════════════════

    [Benchmark(Description = "REAL: LogInformation($\"...\") structured")]
    public void Real_Interpolated_Structured() => _interpolatedReal.LogStructured(42, "Widget", 29.97m);

    [Benchmark(Description = "REAL: LogInformation(\"template\", args)")]
    public void Real_Interpolated_StandardTemplate() => _interpolatedReal.LogStandardTemplate(42, "Widget", 29.97m);

    // ═══════════════════════════════════════════
    // FALLBACK: extension method body (no interceptor) — measures slow-path
    // ═══════════════════════════════════════════

    [Benchmark(Description = "LogInformation($\"...\") FALLBACK (no interceptor)")]
    public void Interpolated_Fallback() => _interpolated.LogFallback(42, "Widget", 29.97m);

    [Benchmark(Description = "LogInformation($\"...\") FALLBACK (level OFF)")]
    public void Interpolated_Fallback_Off() => _interpolatedNull.LogFallback(42, "Widget", 29.97m);

    [Benchmark(Description = "REAL: LogInformation($\"...\") FALLBACK")]
    public void Real_Interpolated_Fallback() => _interpolatedReal.LogFallback(42, "Widget", 29.97m);
}
