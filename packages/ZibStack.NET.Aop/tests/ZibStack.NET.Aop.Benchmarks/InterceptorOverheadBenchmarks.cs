using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;
using ZibStack.NET.Aop;

namespace ZibStack.NET.Aop.Benchmarks;

/// <summary>
/// Measures per-call overhead of runtime aspect handlers vs direct calls.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class AspectOverheadBenchmarks
{
    private TestService _service = null!;

    [GlobalSetup]
    public void Setup()
    {
        _service = new TestService();
        var services = new ServiceCollection();
        services.AddTransient<NoOpHandler>();
        AspectServiceProvider.ServiceProvider = services.BuildServiceProvider();
    }

    [Benchmark(Baseline = true, Description = "Direct call (no AOP)")]
    public int DirectCall() => _service.Add(1, 2);

    [Benchmark(Description = "1 runtime handler")]
    public int OneHandler()
    {
        var handler = AspectServiceProvider.Resolve<NoOpHandler>();
        var ctx = new AspectContext
        {
            ClassName = "TestService",
            MethodName = "Add",
            Parameters = new AspectParameterInfo[]
            {
                new() { Name = "a", Value = 1 },
                new() { Name = "b", Value = 2 },
            }
        };
        handler.OnBefore(ctx);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = _service.Add(1, 2);
        sw.Stop();
        ctx.ElapsedMilliseconds = sw.ElapsedMilliseconds;
        ctx.ReturnValue = result;
        handler.OnAfter(ctx);
        return result;
    }

    [Benchmark(Description = "2 stacked handlers")]
    public int TwoHandlers()
    {
        var h1 = AspectServiceProvider.Resolve<NoOpHandler>();
        var h2 = AspectServiceProvider.Resolve<NoOpHandler>();
        var ctx1 = new AspectContext
        {
            ClassName = "TestService", MethodName = "Add",
            Parameters = new AspectParameterInfo[]
            {
                new() { Name = "a", Value = 1 },
                new() { Name = "b", Value = 2 },
            }
        };
        var ctx2 = new AspectContext
        {
            ClassName = "TestService", MethodName = "Add",
            Parameters = new AspectParameterInfo[]
            {
                new() { Name = "a", Value = 1 },
                new() { Name = "b", Value = 2 },
            }
        };
        h1.OnBefore(ctx1);
        h2.OnBefore(ctx2);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = _service.Add(1, 2);
        sw.Stop();
        ctx2.ElapsedMilliseconds = sw.ElapsedMilliseconds;
        h2.OnAfter(ctx2);
        ctx1.ElapsedMilliseconds = sw.ElapsedMilliseconds;
        h1.OnAfter(ctx1);
        return result;
    }

    [Benchmark(Description = "No params (zero-alloc params)")]
    public void NoParams()
    {
        var handler = AspectServiceProvider.Resolve<NoOpHandler>();
        var ctx = new AspectContext
        {
            ClassName = "TestService", MethodName = "DoNothing",
            Parameters = Array.Empty<AspectParameterInfo>()
        };
        handler.OnBefore(ctx);
        _service.DoNothing();
        handler.OnAfter(ctx);
    }
}

public class TestService
{
    public int Add(int a, int b) => a + b;
    public void DoNothing() { }
}

public class NoOpHandler : IAspectHandler
{
    public void OnBefore(AspectContext context) { }
    public void OnAfter(AspectContext context) { }
    public void OnException(AspectContext context, Exception exception) { }
}
