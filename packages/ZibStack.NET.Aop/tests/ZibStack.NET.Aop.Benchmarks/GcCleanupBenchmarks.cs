using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace ZibStack.NET.Aop.Benchmarks;

/// <summary>
/// Validates ConditionalWeakTable releases entries after GC at scale.
/// Creates many instances, drops references, verifies cleanup.
/// </summary>
[SimpleJob(RuntimeMoniker.Net80, iterationCount: 3, warmupCount: 1)]
[MemoryDiagnoser]
public class GcCleanupBenchmarks
{
    public sealed class FakeHandler { }
    public sealed class ServiceInstance { }

    [Params(10_000, 100_000)]
    public int Count;

    /// <summary>
    /// Populate CWT, drop all instance references, GC, verify cleanup.
    /// If CWT leaks, this will show retained memory.
    /// </summary>
    [Benchmark(Description = "CWT: populate + GC + verify")]
    public int PopulateAndVerifyCleanup()
    {
        var cwt = new ConditionalWeakTable<ServiceInstance, FakeHandler>();
        var weakRefs = new WeakReference[Count];

        for (int i = 0; i < Count; i++)
        {
            var inst = new ServiceInstance();
            weakRefs[i] = new WeakReference(inst);
            cwt.GetValue(inst, static _ => new FakeHandler());
        }
        // inst refs are now out of scope

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        int alive = 0;
        for (int i = 0; i < Count; i++)
            if (weakRefs[i].IsAlive) alive++;

        if (alive > Count / 100) // allow 1% tolerance
            throw new Exception($"CWT leaking: {alive}/{Count} instances survived GC");

        return alive;
    }

    /// <summary>
    /// Simulate churn: create instances, use them, drop half, GC, check.
    /// Mimics real app where services come and go (scoped DI, etc).
    /// </summary>
    [Benchmark(Description = "CWT: churn (create/drop/GC cycles)")]
    public int ChurnSimulation()
    {
        var cwt = new ConditionalWeakTable<ServiceInstance, FakeHandler>();
        int totalAlive = 0;

        for (int cycle = 0; cycle < 5; cycle++)
        {
            var batch = new ServiceInstance[Count / 5];
            for (int i = 0; i < batch.Length; i++)
            {
                batch[i] = new ServiceInstance();
                cwt.GetValue(batch[i], static _ => new FakeHandler());
            }

            // keep only even-indexed instances
            for (int i = 1; i < batch.Length; i += 2)
                batch[i] = null!;

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();

            int alive = 0;
            for (int i = 0; i < batch.Length; i++)
                if (batch[i] != null) alive++;
            totalAlive += alive;
        }

        return totalAlive;
    }
}
