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
    public TestAuthorizationProvider AuthProvider { get; } = new();

    public AopFixture()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Handler);
        services.AddSingleton(AroundHandler);

        // Built-in handlers
        services.AddSingleton<RetryHandler>();
        services.AddSingleton<CacheHandler>();
        services.AddSingleton<MetricsHandler>();
        services.AddSingleton<TimeoutHandler>();
        services.AddSingleton<ValidateHandler>();
        services.AddSingleton<TransactionHandler>();
        services.AddSingleton<IAuthorizationProvider>(AuthProvider);
        services.AddSingleton<AuthorizeHandler>();

        var sp = services.BuildServiceProvider();
        AspectServiceProvider.ServiceProvider = sp;
    }

    public void Dispose()
    {
        CacheHandler.ClearAll();
    }
}

public sealed class TestAuthorizationProvider : IAuthorizationProvider
{
    public HashSet<string> Roles { get; } = new();
    public HashSet<string> Policies { get; } = new();
    public bool IsAuthenticated { get; set; }

    public ValueTask<bool> IsAuthorizedAsync(string policy)
    {
        if (policy == "__authenticated") return new ValueTask<bool>(IsAuthenticated);
        return new ValueTask<bool>(Policies.Contains(policy));
    }

    public ValueTask<bool> IsInRoleAsync(string role) =>
        new ValueTask<bool>(Roles.Contains(role));

    public void Reset()
    {
        Roles.Clear();
        Policies.Clear();
        IsAuthenticated = false;
    }
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

    // ── Generic class ──

    [Fact]
    public void GenericClass_ClassLevelAspect_InterceptsCall()
    {
        var svc = new GenericRepo<string>();
        _ = svc.Get(1);

        Assert.Contains(_handler.Calls, c => c.Phase == "Before" && c.Context.MethodName == "Get");
    }

    // ── Generic method ──

    [Fact]
    public void GenericMethod_MethodLevelAspect_InterceptsCall()
    {
        var svc = new GenericMethodService();
        _ = svc.Fetch<string>(1);

        Assert.Contains(_handler.Calls, c => c.Phase == "Before" && c.Context.MethodName == "Fetch");
    }

    // ── Inheritance ──

    [Fact]
    public void Inheritance_DerivedClass_InheritsAspect()
    {
        var svc = new DerivedAspectService();
        var result = svc.Process(5);

        Assert.Equal(10, result);
        Assert.Contains(_handler.Calls, c => c.Phase == "Before" && c.Context.MethodName == "Process");
    }

    // ── Multiple aspects ──

    [Fact]
    public void MultipleAspects_BothHandlersCalled()
    {
        var svc = new MultiAspectService();
        var result = svc.Work(5);

        Assert.Equal(6, result);
        Assert.Contains(_handler.Calls, c => c.Phase == "Before");
        Assert.True(_aroundHandler.Called);
    }

    // ── Class-level aspect also picks up internal methods ──

    [Fact]
    public void ClassLevelAspect_InterceptsInternalMethod()
    {
        var svc = new MixedAccessService();
        _ = svc.InternalWork(5);

        Assert.Contains(_handler.Calls, c => c.Phase == "Before" && c.Context.MethodName == "InternalWork");
    }

    [Fact]
    public void ClassLevelAspect_StillInterceptsPublicMethod()
    {
        var svc = new MixedAccessService();
        _ = svc.PublicWork(5);

        Assert.Contains(_handler.Calls, c => c.Phase == "Before" && c.Context.MethodName == "PublicWork");
    }

    // ── AOP0001 ground-truth: does the aspect actually fire on a static method? ──
    //
    // These tests are the source of truth for the analyzer claim. If they FAIL
    // (handler.Calls non-empty for a static method), the analyzer is wrong.

    [Fact]
    public void StaticMethod_WithMethodLevelAspect_HandlerNotCalled()
    {
        StaticAspectService.CallCount = 0;
        _handler.Reset();

        StaticAspectService.GetValue(1);
        StaticAspectService.GetValue(1);

        Assert.Equal(2, StaticAspectService.CallCount);                // method ran twice
        Assert.Empty(_handler.Calls);                                  // ← AOP0001 claim
    }

    [Fact]
    public void StaticMethod_WithClassLevelAspect_HandlerNotCalled()
    {
        ClassLevelMixedService.StaticCallCount = 0;
        ClassLevelMixedService.InstanceCallCount = 0;
        _handler.Reset();

        // Verify the class-level aspect fires for INSTANCE method first — sanity check
        // that the fixture is actually wired to the analyzer / generator.
        var svc = new ClassLevelMixedService();
        _ = svc.GetInstance(1);
        Assert.Contains(_handler.Calls, c => c.Phase == "Before" && c.Context.MethodName == "GetInstance");

        // Now the actual AOP0001 claim for class-level + static.
        _handler.Reset();
        ClassLevelMixedService.GetStatic(1);
        Assert.Equal(1, ClassLevelMixedService.StaticCallCount);
        Assert.Empty(_handler.Calls);                                   // ← AOP0001 claim
    }

    // ── AOP0002 ground truth: private method with method-level aspect ────────

    [Fact]
    public void PrivateMethod_WithAspect_HandlerNotCalled()
    {
        var svc = new PrivateAspectService();
        svc.CallCount = 0;
        _handler.Reset();

        _ = svc.CallHidden(7);   // calls private Hidden() internally

        Assert.Equal(1, svc.CallCount);                // Hidden() ran
        Assert.Empty(_handler.Calls);                   // ← AOP0002 claim — handler not called
    }

    // ── AOP0006 ground truth: operator with aspect ──────────────────────────

    [Fact]
    public void Operator_WithAspect_HandlerNotCalled()
    {
        BoxAspect.OperatorCallCount = 0;
        _handler.Reset();

        var c = new BoxAspect { Value = 1 } + new BoxAspect { Value = 2 };

        Assert.Equal(3, c.Value);                       // operator ran
        Assert.Equal(1, BoxAspect.OperatorCallCount);
        Assert.Empty(_handler.Calls);                   // ← AOP0006 claim — handler not called
    }

    // ── AOP0010 ground truth: [Cache] on void method ────────────────────────
    //
    // The original analyzer message claimed "[Cache] has no effect" for void/Task
    // methods. Behavioral test proved the OPPOSITE: the cache DOES intercept —
    // every call after the first is short-circuited and the body never re-runs.
    // The hazard is silent side-effect suppression, not "no effect". Updated
    // AOP0010 message reflects this; this test pins the actual behavior.

    [Fact]
    public void CacheOnVoidMethod_SuppressesSubsequentCalls()
    {
        var svc = new CacheVoidService();
        svc.CallCount = 0;

        svc.DoWork();
        svc.DoWork();
        svc.DoWork();

        Assert.Equal(1, svc.CallCount);   // ← reality: only first call executes the body
    }

    // ── TimeoutHandler ground truth ──────────────────────────────────────────
    //
    // Pins the actual runtime behavior of [Timeout]: the caller receives
    // TimeoutException after the deadline, but the body continues running in the
    // background until it finishes naturally. This is a HANDLER property — the
    // current TimeoutHandler does pure Task.WhenAny and never signals cancellation,
    // so even adding a CancellationToken parameter to the method wouldn't help.
    //
    // (AOP0015 used to fire here suggesting "add a CT param" — that analyzer was
    // removed because the suggestion was misleading: the handler ignores any CT.)

    [Fact]
    public async Task Timeout_AbortsToCallerButLeaksTheCall()
    {
        var svc = new TimeoutNoTokenService();
        svc.CompletedCallCount = 0;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => svc.SlowAsync());
        sw.Stop();

        // The caller sees TimeoutException after ~50ms, NOT the 200ms the body needed.
        Assert.True(sw.ElapsedMilliseconds < 150,
            $"Caller should see TimeoutException quickly, got {sw.ElapsedMilliseconds}ms");

        // The body is still running in background — wait long enough and the counter
        // bumps even though we already got the exception. (Race-prone assertion: give
        // it 300ms to finish.)
        await Task.Delay(300);
        Assert.Equal(1, svc.CompletedCallCount);   // ← background completion proves the leak
    }

    // ── AOP0020 ground truth: aspect method invoked through a delegate ─────

    [Fact]
    public void AspectMethodAsDelegate_HandlerNotCalledWhenInvokedViaDelegate()
    {
        var svc = new DelegateService();
        svc.InvokeCount = 0;
        _handler.Reset();

        // Direct call — interceptor fires (sanity check).
        _ = svc.Direct(1);
        Assert.Single(_handler.Calls.Where(c => c.Phase == "Before"));

        // Now via delegate — interceptor should NOT fire (this is what AOP0020 warns).
        // Suppress the analyzer here since we're INTENTIONALLY exercising the bypass.
        _handler.Reset();
#pragma warning disable AOP0020
        Func<int, int> f = svc.Direct;
#pragma warning restore AOP0020
        _ = f(2);
        _ = f(3);

        Assert.Equal(3, svc.InvokeCount);   // 1 direct + 2 via delegate
        Assert.Empty(_handler.Calls);        // ← AOP0020 claim — delegate path bypasses
    }

    // ── Polly add-on behavioral verification ────────────────────────────────
    //
    // Confirms the main AOP generator's interceptor wires the Polly handlers
    // through the same pipeline as built-in aspects — i.e. nothing about
    // optional packages prevents `[PollyRetry]` etc. from actually running at
    // runtime. Each test uses an isolated DI container with PollyRetryHandler
    // / PollyCircuitBreakerHandler registered, so the Polly resilience pipeline
    // is built inline (no Microsoft.Extensions.Resilience required).

    [Fact]
    public void PollyRetry_RetriesUntilSuccess()
    {
        // Build a fresh DI scope just for this test — we need PollyRetryHandler
        // available alongside the existing RecordingHandler from AopFixture.
        var services = new ServiceCollection();
        services.AddSingleton(new RecordingHandler());
        services.AddSingleton(new AroundRecordingHandler());
        services.AddAopPolly();
        var sp = services.BuildServiceProvider();
        var prevProvider = AspectServiceProvider.ServiceProvider;
        AspectServiceProvider.ServiceProvider = sp;
        try
        {
            var svc = new PollyRetryService();
            svc.CallCount = 0;

            // Succeed on attempt 3 — handler should retry twice (default MaxRetryAttempts=3).
            var result = svc.FlakyMethod(succeedOnAttempt: 3);

            Assert.Equal(3, result);
            Assert.Equal(3, svc.CallCount);
        }
        finally
        {
            AspectServiceProvider.ServiceProvider = prevProvider;
        }
    }

    [Fact]
    public void PollyCircuitBreaker_TripsAfterFailures()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new RecordingHandler());
        services.AddSingleton(new AroundRecordingHandler());
        services.AddAopPolly();
        var sp = services.BuildServiceProvider();
        var prevProvider = AspectServiceProvider.ServiceProvider;
        AspectServiceProvider.ServiceProvider = sp;
        try
        {
            var svc = new PollyCircuitBreakerService();
            svc.CallCount = 0;

            // First two calls bubble the underlying InvalidOperationException, then the
            // breaker opens and subsequent calls throw BrokenCircuitException without
            // invoking the body.
            Assert.Throws<InvalidOperationException>(() => svc.AlwaysFails());
            Assert.Throws<InvalidOperationException>(() => svc.AlwaysFails());

            var beforeBreaker = svc.CallCount;
            // Third call: breaker now open, body must NOT run.
            Assert.ThrowsAny<Exception>(() => svc.AlwaysFails());
            Assert.Equal(beforeBreaker, svc.CallCount);   // body did not execute
        }
        finally
        {
            AspectServiceProvider.ServiceProvider = prevProvider;
        }
    }

    // ── AOP0021 ground truth: NOT runnable, infinite recursion at runtime ───
    //
    // The original AOP0021 analyzer message claimed that base.Method() to an aspect-
    // decorated virtual method "bypasses the interceptor". A runtime experiment proved
    // the OPPOSITE: the call IS intercepted, and because the interceptor body invokes
    // the target through `@this.Method(...)` (virtual dispatch) it lands back in the
    // override, which calls `base.Method()` again — guaranteed StackOverflowException.
    //
    // SOE crashes the test process before any assertions can fire, so we can't run the
    // scenario in this suite. The behavior is still verifiable by hand: enable
    // DerivedBaseCallService.Process and watch the test host die. The fixture remains
    // compiled so the AOP0021 analyzer always has a real call site to flag.
    //
    // The analyzer descriptor and message were updated to reflect the actual mechanism
    // (recursion, not bypass) and bumped to Error severity — see Diagnostics.BaseCall.
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
