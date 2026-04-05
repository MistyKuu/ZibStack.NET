using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace ZibStack.NET.Aop.Benchmarks;

/// <summary>
/// Validates that ConditionalWeakTable actually releases entries after GC.
/// Not a perf benchmark — more of a correctness/memory safety verification.
/// Run manually to see console output.
/// </summary>
[SimpleJob(RuntimeMoniker.Net100, iterationCount: 1, warmupCount: 0)]
public class GcCleanupBenchmarks
{
    public sealed class FakeHandler { }
    public sealed class ServiceInstance { }

    [Benchmark(Description = "CWT: verify GC cleanup")]
    public void VerifyGcCleanup()
    {
        var cwt = new ConditionalWeakTable<ServiceInstance, FakeHandler>();
        var weakRefs = new WeakReference[1000];

        // Populate
        for (int i = 0; i < 1000; i++)
        {
            var inst = new ServiceInstance();
            weakRefs[i] = new WeakReference(inst);
            cwt.GetValue(inst, static _ => new FakeHandler());
        }

        // All instances go out of scope — force GC
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        int alive = 0;
        for (int i = 0; i < 1000; i++)
            if (weakRefs[i].IsAlive) alive++;

        // If CWT held strong refs, all 1000 would be alive.
        // With weak refs, most/all should be collected.
        Console.WriteLine($"[CWT GC test] Alive after GC: {alive}/1000 (should be ~0)");
        if (alive > 10)
            throw new Exception($"CWT is leaking! {alive} instances survived GC.");
    }
}
