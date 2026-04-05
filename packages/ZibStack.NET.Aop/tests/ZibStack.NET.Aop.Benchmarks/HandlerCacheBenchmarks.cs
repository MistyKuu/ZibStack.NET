using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace ZibStack.NET.Aop.Benchmarks;

/// <summary>
/// Compares handler caching strategies — the CWT lookup cost on the hot path.
/// Uses multiple distinct instances to stress the cache at realistic scale.
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

    public sealed class FakeHandler
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnBefore() { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnAfter() { }
    }

    public sealed class ServiceInstance { }

    private static FakeHandler? _staticCached;
    private static readonly ConditionalWeakTable<ServiceInstance, FakeHandler> _cwt = new();
    private static FakeHandler Resolve() => new();

    private ServiceInstance[] _instances = null!;

    [Params(100, 10_000, 100_000)]
    public int InstanceCount;

    [GlobalSetup]
    public void Setup()
    {
        _staticCached = Resolve();
        _instances = new ServiceInstance[InstanceCount];
        for (int i = 0; i < InstanceCount; i++)
        {
            _instances[i] = new ServiceInstance();
            _cwt.GetValue(_instances[i], static _ => Resolve());
        }
    }

    /// <summary>
    /// Static field: single null-check, shared handler.
    /// This is the cheapest possible — reference point.
    /// </summary>
    [Benchmark(Description = "Static field (shared)")]
    public FakeHandler Lookup_Static()
    {
        return _staticCached ??= Resolve();
    }

    /// <summary>
    /// CWT lookup for a single instance (best case — same key every time).
    /// </summary>
    [Benchmark(Description = "CWT single instance")]
    public FakeHandler Lookup_CWT_Single()
    {
        return _cwt.GetValue(_instances[0], static _ => Resolve());
    }

    /// <summary>
    /// CWT lookup cycling through ALL instances (worst case — cache pressure).
    /// This simulates a request-per-object scenario with many live instances.
    /// Reported as ops/instance.
    /// </summary>
    [Benchmark(Description = "CWT scan all instances", Baseline = true, OperationsPerInvoke = 1)]
    public int Lookup_CWT_ScanAll()
    {
        int sum = 0;
        var instances = _instances;
        for (int i = 0; i < instances.Length; i++)
            sum += _cwt.GetValue(instances[i], static _ => Resolve()).GetHashCode();
        return sum;
    }
}
