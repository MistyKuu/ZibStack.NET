using System.Diagnostics;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace ZibStack.NET.Aop.Benchmarks;

/// <summary>
/// Simulates the FULL generated interceptor code path.
///
/// Per-call allocations for a runtime handler (1 aspect, 2 params):
///   - AspectContext:           ~72B  (object + 5 properties + Dictionary in Properties)
///   - Dictionary(Properties):  ~120B (empty dict, allocated eagerly)
///   - AspectParameterInfo[]:   ~40B  (array header + 2 refs)
///   - AspectParameterInfo ×2:  ~96B  (2 objects × 48B)
///   - Boxing (value types):    ~24B  per value-type param
///   - Stopwatch.StartNew():    ~40B
///   Total: ~420B per call per aspect
///
/// Inline emitters (like [Log]) generate direct code with no AspectContext —
/// zero-alloc via LoggerMessage.Define delegates. Only runtime handlers pay this cost.
///
/// This benchmark answers:
///   1. What is the per-call overhead of runtime handler interception?
///   2. How does it scale at 100K+ calls/sec?
///   3. How much GC pressure does the AspectContext allocation create?
/// </summary>
[MemoryDiagnoser]
[Config(typeof(Config))]
public class InterceptorOverheadBenchmarks
{
    private class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.MediumRun);
            AddColumn(StatisticColumn.P95);
        }
    }

    // --- Simulated types matching real generated code ---

    public sealed class OrderService
    {
        private int _counter;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int GetOrder(int id) => ++_counter + id;
    }

    // Mirrors IAspectHandler — NoInlining to prevent JIT from optimizing away
    public sealed class TimingHandler
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnBefore(AspectContext ctx) { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnAfter(AspectContext ctx) { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnException(AspectContext ctx, Exception ex) { }
    }

    public sealed class LogHandler
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnBefore(AspectContext ctx) { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnAfter(AspectContext ctx) { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnException(AspectContext ctx, Exception ex) { }
    }

    // Mirrors the real AspectContext — same fields, same Dictionary in Properties
    public sealed class AspectContext
    {
        public string ClassName { get; set; } = "";
        public string MethodName { get; set; } = "";
        public AspectParameterInfo[] Parameters { get; set; } = [];
        public object? ReturnValue { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>();
    }

    public sealed class AspectParameterInfo
    {
        public required string Name { get; init; }
        public required object? Value { get; init; }
        public bool IsSensitive { get; init; }
        public bool IsNoLog { get; init; }
    }

    // --- CWT caches (exactly what generated code emits) ---

    private static readonly ConditionalWeakTable<OrderService, TimingHandler> _timingCache = new();
    private static readonly ConditionalWeakTable<OrderService, LogHandler> _logCache = new();
    private static readonly TimingHandler _sharedTimingHandler = new();
    private static readonly LogHandler _sharedLogHandler = new();

    private OrderService _service = null!;
    private OrderService[] _manyServices = null!;

    [Params(1, 10, 100)]
    public int ServiceInstances;

    [GlobalSetup]
    public void Setup()
    {
        _service = new OrderService();
        _manyServices = new OrderService[ServiceInstances];
        for (int i = 0; i < ServiceInstances; i++)
        {
            _manyServices[i] = new OrderService();
            _timingCache.GetValue(_manyServices[i], static _ => _sharedTimingHandler);
            _logCache.GetValue(_manyServices[i], static _ => _sharedLogHandler);
        }
        _timingCache.GetValue(_service, static _ => _sharedTimingHandler);
        _logCache.GetValue(_service, static _ => _sharedLogHandler);
    }

    // ======== Single-call benchmarks (allocation profile) ========

    /// <summary>Direct method call — no interception. Baseline.</summary>
    [Benchmark(Description = "Direct call (no AOP)", Baseline = true)]
    public int DirectCall()
    {
        return _service.GetOrder(42);
    }

    /// <summary>
    /// Mirrors generated code for [Timing] on GetOrder(int id).
    /// Shows per-call allocations: AspectContext + AspectParameterInfo[] + Stopwatch + boxing.
    /// </summary>
    [Benchmark(Description = "1 aspect (full pipeline)")]
    public int Intercepted_SingleAspect()
    {
        var @this = _service;

        // CWT lookup (cached)
        var handler = _timingCache.GetValue(@this, static _ => _sharedTimingHandler);

        // Per-call allocations: context + params + dictionary
        var ctx = new AspectContext
        {
            ClassName = "OrderService",
            MethodName = "GetOrder",
            Parameters =
            [
                new AspectParameterInfo { Name = "id", Value = 42 } // boxing: int → object
            ]
        };

        handler.OnBefore(ctx);

        var sw = Stopwatch.StartNew();
        try
        {
            var result = @this.GetOrder(42);
            sw.Stop();
            ctx.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            ctx.ReturnValue = result; // boxing
            handler.OnAfter(ctx);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            ctx.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            handler.OnException(ctx, ex);
            throw;
        }
    }

    /// <summary>
    /// Two stacked aspects: [Timing] + [Log].
    /// 2× context + 2× params + 2× dictionary + boxing + stopwatch.
    /// </summary>
    [Benchmark(Description = "2 aspects (full pipeline)")]
    public int Intercepted_TwoAspects()
    {
        var @this = _service;

        var timingHandler = _timingCache.GetValue(@this, static _ => _sharedTimingHandler);
        var logHandler = _logCache.GetValue(@this, static _ => _sharedLogHandler);

        var ctx1 = new AspectContext
        {
            ClassName = "OrderService",
            MethodName = "GetOrder",
            Parameters = [new AspectParameterInfo { Name = "id", Value = 42 }]
        };
        var ctx2 = new AspectContext
        {
            ClassName = "OrderService",
            MethodName = "GetOrder",
            Parameters = [new AspectParameterInfo { Name = "id", Value = 42 }]
        };

        timingHandler.OnBefore(ctx1);
        logHandler.OnBefore(ctx2);

        var sw = Stopwatch.StartNew();
        try
        {
            var result = @this.GetOrder(42);
            sw.Stop();
            ctx1.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            ctx1.ReturnValue = result;
            ctx2.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            ctx2.ReturnValue = result;
            logHandler.OnAfter(ctx2);
            timingHandler.OnAfter(ctx1);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            ctx1.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            ctx2.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            logHandler.OnException(ctx2, ex);
            timingHandler.OnException(ctx1, ex);
            throw;
        }
    }

    // ======== High-throughput (GC pressure at scale) ========

    /// <summary>
    /// 100K intercepted calls across N instances.
    /// Shows total GC pressure from AspectContext churn.
    /// </summary>
    [Benchmark(Description = "100K calls (1 aspect)")]
    [Arguments(100_000)]
    public int HighThroughput_SingleAspect(int callCount)
    {
        int sum = 0;
        var services = _manyServices;
        for (int i = 0; i < callCount; i++)
        {
            var @this = services[i % services.Length];
            var handler = _timingCache.GetValue(@this, static _ => _sharedTimingHandler);

            var ctx = new AspectContext
            {
                ClassName = "OrderService",
                MethodName = "GetOrder",
                Parameters = [new AspectParameterInfo { Name = "id", Value = i }]
            };

            handler.OnBefore(ctx);

            var sw = Stopwatch.StartNew();
            var result = @this.GetOrder(i);
            sw.Stop();

            ctx.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            ctx.ReturnValue = result;
            handler.OnAfter(ctx);

            sum += result;
        }
        return sum;
    }

    /// <summary>
    /// 100K direct calls (no interception) — baseline for throughput comparison.
    /// </summary>
    [Benchmark(Description = "100K calls (direct, no AOP)")]
    [Arguments(100_000)]
    public int HighThroughput_Direct(int callCount)
    {
        int sum = 0;
        var services = _manyServices;
        for (int i = 0; i < callCount; i++)
        {
            var @this = services[i % services.Length];
            sum += @this.GetOrder(i);
        }
        return sum;
    }
}
