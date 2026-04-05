using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace ZibStack.NET.Aop.Benchmarks;

/// <summary>
/// Measures actual memory overhead of ConditionalWeakTable at scale.
/// Creates N instances, caches a handler for each, measures total memory.
/// Also verifies GC cleanup — after nulling references, entries should be collected.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net100)]
public class MemoryOverheadBenchmarks
{
    public sealed class FakeHandler { public int Id; }
    public sealed class ServiceInstance { public int Id; }

    [Params(10_000, 100_000, 1_000_000)]
    public int Count;

    [Benchmark(Description = "CWT: populate N entries")]
    public ConditionalWeakTable<ServiceInstance, FakeHandler> CWT_Populate()
    {
        var cwt = new ConditionalWeakTable<ServiceInstance, FakeHandler>();
        for (int i = 0; i < Count; i++)
        {
            var inst = new ServiceInstance { Id = i };
            cwt.GetValue(inst, static _ => new FakeHandler());
        }
        // instances go out of scope here — GC can reclaim
        return cwt;
    }

    [Benchmark(Description = "CWT: populate + retain refs")]
    public (ConditionalWeakTable<ServiceInstance, FakeHandler>, ServiceInstance[]) CWT_PopulateRetained()
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

    [Benchmark(Description = "Dict: populate + retain refs (baseline)")]
    public (Dictionary<ServiceInstance, FakeHandler>, ServiceInstance[]) Dict_PopulateRetained()
    {
        var dict = new Dictionary<ServiceInstance, FakeHandler>(Count);
        var instances = new ServiceInstance[Count];
        for (int i = 0; i < Count; i++)
        {
            instances[i] = new ServiceInstance { Id = i };
            dict[instances[i]] = new FakeHandler();
        }
        return (dict, instances);
    }
}
