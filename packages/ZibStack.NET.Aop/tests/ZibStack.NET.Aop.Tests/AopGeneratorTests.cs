using Microsoft.Extensions.DependencyInjection;
using ZibStack.NET.Aop;
using ZibStack.NET.Aop.Tests.Fixtures;
using Xunit;

namespace ZibStack.NET.Aop.Tests;

// ── Serialize all tests that touch the static AspectServiceProvider ─────────

[CollectionDefinition("Aop")]
public class AopCollection : ICollectionFixture<AopFixture> { }

public class AopFixture : IDisposable
{
    public RecordingHandler Handler { get; } = new();
    public AroundRecordingHandler AroundHandler { get; } = new();

    public AopFixture()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Handler);
        services.AddSingleton(AroundHandler);
        var sp = services.BuildServiceProvider();
        AspectServiceProvider.ServiceProvider = sp;
    }

    public void Dispose() { }
}

// ── Behavioral tests — verify aspects actually fire at runtime ──────────────

[Collection("Aop")]
public class AopBehaviorTests
{
    private readonly RecordingHandler _handler;
    private readonly AroundRecordingHandler _aroundHandler;

    public AopBehaviorTests(AopFixture fixture)
    {
        _handler = fixture.Handler;
        _aroundHandler = fixture.AroundHandler;
        _handler.Reset();
        _aroundHandler.Reset();
    }

    // ── Method-level aspect ──

    [Fact]
    public void MethodLevelAspect_CallsOnBeforeAndOnAfter()
    {
        var svc = new MethodAspectService();
        var result = svc.Add(2, 3);

        Assert.Equal(5, result);
        Assert.Contains(_handler.Calls, c => c.Phase == "Before");
        Assert.Contains(_handler.Calls, c => c.Phase == "After");
    }

    [Fact]
    public void MethodWithoutAspect_DoesNotCallHandler()
    {
        _handler.Reset();
        var svc = new MethodAspectService();
        _ = svc.Plain(42);

        Assert.Empty(_handler.Calls);
    }

    // ── Class-level aspect ──

    [Fact]
    public void ClassLevelAspect_InterceptsAllMethods()
    {
        var svc = new ClassAspectService();
        _ = svc.Multiply(3, 4);

        Assert.Contains(_handler.Calls, c => c.Phase == "Before" && c.Context.MethodName == "Multiply");
        Assert.Contains(_handler.Calls, c => c.Phase == "After" && c.Context.MethodName == "Multiply");

        _handler.Reset();
        _ = svc.Echo("hi");

        Assert.Contains(_handler.Calls, c => c.Phase == "Before" && c.Context.MethodName == "Echo");
    }

    // ── Async methods ──

    [Fact]
    public async Task AsyncMethod_CallsHandler()
    {
        var svc = new AsyncAspectService();
        var result = await svc.ComputeAsync(5);

        Assert.Equal(10, result);
        Assert.Contains(_handler.Calls, c => c.Phase == "Before");
        Assert.Contains(_handler.Calls, c => c.Phase == "After");
    }

    // ── Exception path ──

    [Fact]
    public void Exception_CallsOnException()
    {
        var svc = new ThrowingAspectService();
        Assert.Throws<InvalidOperationException>(() => svc.Fail());

        Assert.Contains(_handler.Calls, c => c.Phase == "Before");
        Assert.Contains(_handler.Calls, c => c.Phase == "Exception");
        Assert.DoesNotContain(_handler.Calls, c => c.Phase == "After");
    }

    // ── Around handler ──

    [Fact]
    public void AroundHandler_WrapsExecution()
    {
        var svc = new AroundAspectService();
        var result = svc.Double(7);

        Assert.Equal(14, result);
        Assert.True(_aroundHandler.Called);
        Assert.True(_aroundHandler.ProceedCalled);
    }

    // ── Context carries correct metadata ──

    [Fact]
    public void Context_HasCorrectClassAndMethodName()
    {
        var svc = new MethodAspectService();
        _ = svc.Add(1, 2);

        var ctx = _handler.Calls.First(c => c.Phase == "Before").Context;
        Assert.Equal("MethodAspectService", ctx.ClassName);
        Assert.Equal("Add", ctx.MethodName);
    }

    [Fact]
    public void Context_HasParameterValues()
    {
        var svc = new MethodAspectService();
        _ = svc.Add(10, 20);

        var ctx = _handler.Calls.First(c => c.Phase == "Before").Context;
        Assert.Equal(2, ctx.Parameters.Count);
        Assert.Equal("a", ctx.Parameters[0].Name);
        Assert.Equal(10, ctx.Parameters[0].Value);
        Assert.Equal("b", ctx.Parameters[1].Name);
        Assert.Equal(20, ctx.Parameters[1].Value);
    }

    [Fact]
    public void Context_AfterHasReturnValue()
    {
        var svc = new MethodAspectService();
        _ = svc.Add(3, 7);

        var ctx = _handler.Calls.First(c => c.Phase == "After").Context;
        Assert.Equal(10, ctx.ReturnValue);
    }

    [Fact]
    public void Context_AfterHasElapsedMilliseconds()
    {
        var svc = new MethodAspectService();
        _ = svc.Add(1, 1);

        var ctx = _handler.Calls.First(c => c.Phase == "After").Context;
        Assert.True(ctx.ElapsedMilliseconds >= 0);
    }

    // ── Interface proxy ──

    [Fact]
    public void InterfaceProxy_ClassLevelAspect_InterceptsCall()
    {
        IOrderAspectService svc = new OrderAspectServiceImpl();
        var result = svc.GetOrder(42);

        Assert.Equal(42, result);
        Assert.Contains(_handler.Calls, c => c.Phase == "Before");
        Assert.Contains(_handler.Calls, c => c.Phase == "After");
    }

    [Fact]
    public void InterfaceProxy_MethodLevelAspect_InterceptsCall()
    {
        ISelectiveService svc = new SelectiveServiceImpl();
        var result = svc.Tracked(5);

        Assert.Equal(5, result);
        Assert.Contains(_handler.Calls, c => c.Phase == "Before" && c.Context.MethodName == "Tracked");
    }

    [Fact]
    public void InterfaceProxy_MethodWithoutAspect_NotIntercepted()
    {
        _handler.Reset();
        ISelectiveService svc = new SelectiveServiceImpl();
        _ = svc.Untracked(5);

        Assert.Empty(_handler.Calls);
    }
}

// ── Pure runtime tests (no generator needed) ────────────────────────────────

[Collection("Aop")]
public class AspectContextTests
{
    [Fact]
    public void FormatParameters_MasksSensitive_SkipsNoLog()
    {
        var ctx = new AspectContext
        {
            ClassName = "Svc", MethodName = "Login",
            Parameters = new AspectParameterInfo[]
            {
                new() { Name = "user", Value = "admin" },
                new() { Name = "pass", Value = "secret", IsSensitive = true },
                new() { Name = "data", Value = null, IsNoLog = true },
            }
        };

        var formatted = ctx.FormatParameters();
        Assert.Equal("user: admin, pass: ***", formatted);
        Assert.DoesNotContain("data", formatted);
        Assert.DoesNotContain("secret", formatted);
    }

    [Fact]
    public void Resolve_ThrowsWhenNotConfigured()
    {
        var prev = AspectServiceProvider.ServiceProvider;
        try
        {
            AspectServiceProvider.ServiceProvider = null;
            Assert.Throws<InvalidOperationException>(() => AspectServiceProvider.Resolve<object>());
        }
        finally
        {
            AspectServiceProvider.ServiceProvider = prev;
        }
    }
}
