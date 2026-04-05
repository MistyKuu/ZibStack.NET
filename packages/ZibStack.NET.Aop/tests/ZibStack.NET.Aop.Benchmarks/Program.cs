using BenchmarkDotNet.Running;
using ZibStack.NET.Aop.Benchmarks;

var switcher = BenchmarkSwitcher.FromTypes(
[
    typeof(HandlerCacheBenchmarks),
    typeof(MemoryOverheadBenchmarks),
    typeof(GcCleanupBenchmarks),
]);

switcher.Run(args);
