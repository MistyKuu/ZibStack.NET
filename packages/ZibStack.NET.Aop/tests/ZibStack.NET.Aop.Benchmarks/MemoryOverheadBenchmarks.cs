using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace ZibStack.NET.Aop.Benchmarks;

/// <summary>
/// Measures memory overhead of ConditionalWeakTable at realistic scale.
/// Answers: "if I have 100K/1M live service instances, how much extra memory
/// does the per-instance handler cache cost?"
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net100)]
public class MemoryOverheadBenchmarks
{
    public sealed class FakeHandler { public int Id; }
    public sealed class ServiceInstance { public int Id; }

    [Params(10_000, 100_000, 1_000_000)]
    public int Count;

    /// <summary>
    /// Baseline: just allocating the instances, no cache at all.
    /// </summary>
    [Benchmark(Description = "Baseline: instances only", Baseline = true)]
    public ServiceInstance[] Baseline_NoCache()
    {
        var instances = new ServiceInstance[Count];
        for (int i = 0; i < Count; i++)
            instances[i] = new ServiceInstance { Id = i };
        return instances;
    }

    /// <summary>
    /// CWT with all instances retained (worst case — full pressure).
    /// Difference from baseline = CWT overhead.
    /// </summary>
    [Benchmark(Description = "CWT: instances + cache")]
    public (ConditionalWeakTable<ServiceInstance, FakeHandler>, ServiceInstance[]) CWT_Retained()
    {
        var cwt = new ConditionalWeakTable<ServiceInstance, FakeHandler>();
        var instances = new ServiceInstance[Count];
        for (int i = 0; i < Count; i++)
        {
            instances[i] = new ServiceInstance { Id = i };
            cwt.GetValue(instances[i], static _ => new FakeHandler());
        }
        return (cwt, instances);
    }

    /// <summary>
    /// CWT with 2 handler types per instance (like [Timing] + [Log] stacked).
    /// </summary>
    [Benchmark(Description = "CWT x2: two handler caches")]
    public (ConditionalWeakTable<ServiceInstance, FakeHandler>, ConditionalWeakTable<ServiceInstance, FakeHandler>, ServiceInstance[]) CWT_TwoHandlers()
    {
        var cwt1 = new ConditionalWeakTable<ServiceInstance, FakeHandler>();
        var cwt2 = new ConditionalWeakTable<ServiceInstance, FakeHandler>();
        var instances = new ServiceInstance[Count];
        for (int i = 0; i < Count; i++)
        {
            instances[i] = new ServiceInstance { Id = i };
            cwt1.GetValue(instances[i], static _ => new FakeHandler());
            cwt2.GetValue(instances[i], static _ => new FakeHandler());
        }
        return (cwt1, cwt2, instances);
    }
}
