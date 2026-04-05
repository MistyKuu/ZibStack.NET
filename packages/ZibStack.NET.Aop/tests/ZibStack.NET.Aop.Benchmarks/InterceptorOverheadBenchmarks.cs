using System.Diagnostics;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace ZibStack.NET.Aop.Benchmarks;

/// <summary>
/// Simulates the FULL generated interceptor code path at scale.
/// Measures the combined overhead of everything the interceptor does:
///   - ConditionalWeakTable handler lookup
///   - AspectContext + AspectParameterInfo[] allocation
///   - Stopwatch start/stop
///   - handler.OnBefore / handler.OnAfter calls
///
/// Compares:
///   - Direct call (no interception)
///   - Intercepted call (simulated generated code)
///   - Intercepted with 2 aspects stacked
///
/// This answers: "what does interception cost per call at 100K-1M calls/sec?"
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

    // --- Simulated service + handler (mirrors real generated code) ---

    public sealed class OrderService
    {
        private int _counter;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int GetOrder(int id) => ++_counter + id;
    }

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

    // --- Simulated AspectContext (same shape as real one) ---

    public sealed class AspectContext
    {
        public string ClassName { get; set; } = "";
        public string MethodName { get; set; } = "";
        public AspectParameterInfo[] Parameters { get; set; } = [];
        public object? ReturnValue { get; set; }
        public long ElapsedMilliseconds { get; set; }
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

    // pre-resolved handlers (simulates DI)
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
            // warm up CWT
            _timingCache.GetValue(_manyServices[i], static _ => _sharedTimingHandler);
            _logCache.GetValue(_manyServices[i], static _ => _sharedLogHandler);
        }
        _timingCache.GetValue(_service, static _ => _sharedTimingHandler);
        _logCache.GetValue(_service, static _ => _sharedLogHandler);
    }

    // === Benchmark: direct call (no interception) ===

    [Benchmark(Description = "Direct call (no AOP)", Baseline = true)]
    public int DirectCall()
    {
        return _service.GetOrder(42);
    }

    // === Benchmark: simulated interceptor — 1 aspect ===
    // This is what the source generator produces for a single [Timing] attribute.

    [Benchmark(Description = "Intercepted (1 aspect)")]
    public int Intercepted_SingleAspect()
    {
        var @this = _service;

        // handler lookup (CWT)
        var handler = _timingCache.GetValue(@this, static _ => _sharedTimingHandler);

        // context allocation
        var ctx = new AspectContext
        {
            ClassName = "OrderService",
            MethodName = "GetOrder",
            Parameters = [new AspectParameterInfo { Name = "id", Value = 42 }]
        };

        handler.OnBefore(ctx);

        var sw = Stopwatch.StartNew();
        try
        {
            var result = @this.GetOrder(42);
            sw.Stop();
            ctx.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            ctx.ReturnValue = result;
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

    // === Benchmark: simulated interceptor — 2 stacked aspects ===

    [Benchmark(Description = "Intercepted (2 aspects)")]
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

    // === Benchmark: high-throughput — 100K calls across many instances ===

    [Benchmark(Description = "100K calls across N instances")]
    [Arguments(100_000)]
    public int HighThroughput(int callCount)
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
}
