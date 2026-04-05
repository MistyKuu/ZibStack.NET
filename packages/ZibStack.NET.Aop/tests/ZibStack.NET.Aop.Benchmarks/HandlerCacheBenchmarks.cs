using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace ZibStack.NET.Aop.Benchmarks;

/// <summary>
/// Compares handler caching strategies for the AOP interceptor:
///   1. Static field   — one handler shared across all instances (old approach)
///   2. CWT            — ConditionalWeakTable, per-instance cache (current approach)
///   3. ConcurrentDict — ConcurrentDictionary keyed by instance, for reference
///
/// Benchmarks both the lookup (hot path) and memory overhead at scale.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(Config))]
public class HandlerCacheBenchmarks
{
    private class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.ShortRun);
            AddColumn(StatisticColumn.P95);
        }
    }

    // Simulated handler — lightweight, like TimingHandler
    public sealed class FakeHandler
    {
        public int Value;
        public void Handle() => Value++;
    }

    // Simulated service instances (like OrderService, UserService, etc.)
    public sealed class ServiceInstance { }

    // === Caching strategies ===

    private static FakeHandler? _staticCached;
    private static readonly ConditionalWeakTable<ServiceInstance, FakeHandler> _cwt = new();
    private static readonly ConcurrentDictionary<ServiceInstance, FakeHandler> _concurrentDict = new();

    private static FakeHandler Resolve() => new();

    // Pre-allocated instances for benchmarks
    private ServiceInstance[] _instances = null!;
    private ServiceInstance _singleInstance = null!;

    [Params(1, 100, 10_000, 1_000_000)]
    public int InstanceCount;

    [GlobalSetup]
    public void Setup()
    {
        _singleInstance = new ServiceInstance();
        _instances = new ServiceInstance[InstanceCount];
        for (int i = 0; i < InstanceCount; i++)
            _instances[i] = new ServiceInstance();

        // Warm up all caches
        _staticCached = Resolve();
        foreach (var inst in _instances)
        {
            _cwt.GetValue(inst, static _ => Resolve());
            _concurrentDict.GetOrAdd(inst, static _ => Resolve());
        }
    }

    // === Hot path: lookup existing handler (the common case in real apps) ===

    [Benchmark(Description = "Static field (shared)")]
    public FakeHandler Lookup_Static()
    {
        return _staticCached ??= Resolve();
    }

    [Benchmark(Description = "CWT.GetValue (per-instance)", Baseline = true)]
    public FakeHandler Lookup_CWT()
    {
        return _cwt.GetValue(_singleInstance, static _ => Resolve());
    }

    [Benchmark(Description = "ConcurrentDict.GetOrAdd")]
    public FakeHandler Lookup_ConcurrentDict()
    {
        return _concurrentDict.GetOrAdd(_singleInstance, static _ => Resolve());
    }

    // === Scan: iterate over all instances and get handler (simulates many objects) ===

    [Benchmark(Description = "CWT scan all instances")]
    public int Scan_CWT()
    {
        int sum = 0;
        foreach (var inst in _instances)
            sum += _cwt.GetValue(inst, static _ => Resolve()).Value;
        return sum;
    }

    [Benchmark(Description = "ConcurrentDict scan all instances")]
    public int Scan_ConcurrentDict()
    {
        int sum = 0;
        foreach (var inst in _instances)
            sum += _concurrentDict.GetOrAdd(inst, static _ => Resolve()).Value;
        return sum;
    }
}
