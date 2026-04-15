using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using ZibStack.NET.Aop.Analyzers;
using ZibStack.NET.Aop.CodeFixes;

namespace ZibStack.NET.Aop.Analyzers.Tests;

public class CodeFixTests
{
    private const string AopStubs = @"
namespace ZibStack.NET.Aop
{
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public class AspectAttribute : System.Attribute { public int Order { get; set; } }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class CacheAttribute : AspectAttribute
    {
        public int DurationSeconds { get; set; } = 300;
        public string? KeyTemplate { get; set; }
    }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class RetryAttribute : AspectAttribute
    {
        public int MaxAttempts { get; set; } = 3;
        public int DelayMs { get; set; }
        public double BackoffMultiplier { get; set; } = 1.0;
    }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class TimeoutAttribute : AspectAttribute
    {
        public int TimeoutMs { get; set; } = 30000;
    }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class ValidateAttribute : AspectAttribute { }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class MyAspectAttribute : AspectAttribute { }
}
";

    // ── MakeMethodInternalCodeFix (AOP0002) ─────────────────────────────────

    [Fact]
    public async Task PrivateMethodWithAspect_FixedToInternal()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [MyAspect]
    private void {|#0:DoWork|}() { }
}
" + AopStubs;

        var fixedCode = @"
using ZibStack.NET.Aop;

public class Svc
{
    [MyAspect]
    internal void DoWork() { }
}
" + AopStubs;

        var test1 = new CSharpCodeFixTest<AspectMethodAnalyzer, MakeMethodInternalCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.PrivateOrProtected)
                .WithLocation(0)
                .WithArguments("MyAspectAttribute", "DoWork"));

        await test1.RunAsync();
    }

    [Fact]
    public async Task ProtectedMethodWithAspect_FixedToInternal()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [MyAspect]
    protected void {|#0:DoWork|}() { }
}
" + AopStubs;

        var fixedCode = @"
using ZibStack.NET.Aop;

public class Svc
{
    [MyAspect]
    internal void DoWork() { }
}
" + AopStubs;

        var test1 = new CSharpCodeFixTest<AspectMethodAnalyzer, MakeMethodInternalCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.PrivateOrProtected)
                .WithLocation(0)
                .WithArguments("MyAspectAttribute", "DoWork"));

        await test1.RunAsync();
    }

    // ── FixRetryMaxAttemptsCodeFix (AOP0011) ────────────────────────────────

    [Fact]
    public async Task RetryMaxAttemptsZero_FixedToThree()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [{|#0:Retry(MaxAttempts = 0)|}]
    public int Get() => 1;
}
" + AopStubs;

        var fixedCode = @"
using ZibStack.NET.Aop;

public class Svc
{
    [Retry(MaxAttempts = 3)]
    public int Get() => 1;
}
" + AopStubs;

        var test1 = new CSharpCodeFixTest<BuiltInAspectArgumentAnalyzer, FixRetryMaxAttemptsCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.RetryMaxAttempts)
                .WithLocation(0)
                .WithArguments(0));

        await test1.RunAsync();
    }

    // ── RemoveAttributeCodeFix (AOP0001 / AOP0010 / AOP0016) ────────────────

    [Fact]
    public async Task StaticMethodWithAspect_FixedByRemovingAspect()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [MyAspect]
    public static void {|#0:DoWork|}() { }
}
" + AopStubs;

        var fixedCode = @"
using ZibStack.NET.Aop;

public class Svc
{
    public static void DoWork() { }
}
" + AopStubs;

        var test1 = new CSharpCodeFixTest<AspectMethodAnalyzer, RemoveAttributeCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.StaticMethod)
                .WithLocation(0)
                .WithArguments("MyAspectAttribute", "DoWork"));

        await test1.RunAsync();
    }

    [Fact]
    public async Task CacheOnVoidMethod_FixedByRemovingCache()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [{|#0:Cache|}]
    public void DoWork() { }
}
" + AopStubs;

        var fixedCode = @"
using ZibStack.NET.Aop;

public class Svc
{
    public void DoWork() { }
}
" + AopStubs;

        var test1 = new CSharpCodeFixTest<BuiltInAspectArgumentAnalyzer, RemoveAttributeCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.CacheNonReturning)
                .WithLocation(0)
                .WithArguments("DoWork"));

        await test1.RunAsync();
    }

    [Fact]
    public async Task ValidateNoParams_FixedByRemovingValidate()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [{|#0:Validate|}]
    public int Get() => 1;
}
" + AopStubs;

        var fixedCode = @"
using ZibStack.NET.Aop;

public class Svc
{
    public int Get() => 1;
}
" + AopStubs;

        var test1 = new CSharpCodeFixTest<BuiltInAspectArgumentAnalyzer, RemoveAttributeCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.ValidateNoParameters)
                .WithLocation(0)
                .WithArguments("Get"));

        await test1.RunAsync();
    }

    // ── FixRetryDelayCodeFix (AOP0012) ──────────────────────────────────────

    [Fact]
    public async Task RetryDelayNegative_FixedToZero()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [{|#0:Retry(DelayMs = -100)|}]
    public int Get() => 1;
}
" + AopStubs;

        var fixedCode = @"
using ZibStack.NET.Aop;

public class Svc
{
    [Retry(DelayMs = 0)]
    public int Get() => 1;
}
" + AopStubs;

        var test1 = new CSharpCodeFixTest<BuiltInAspectArgumentAnalyzer, FixRetryDelayCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.RetryDelay)
                .WithLocation(0)
                .WithArguments(-100));

        await test1.RunAsync();
    }

    // ── FixRetryBackoffCodeFix (AOP0013) ────────────────────────────────────

    [Fact]
    public async Task RetryBackoffShrinking_FixedToOne()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [{|#0:Retry(BackoffMultiplier = 0.5)|}]
    public int Get() => 1;
}
" + AopStubs;

        var fixedCode = @"
using ZibStack.NET.Aop;

public class Svc
{
    [Retry(BackoffMultiplier = 1.0)]
    public int Get() => 1;
}
" + AopStubs;

        var test1 = new CSharpCodeFixTest<BuiltInAspectArgumentAnalyzer, FixRetryBackoffCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.RetryBackoff)
                .WithLocation(0)
                .WithArguments(0.5));

        await test1.RunAsync();
    }

    // ── FixTimeoutValueCodeFix (AOP0014) ────────────────────────────────────

    [Fact]
    public async Task TimeoutZero_FixedToDefault()
    {
        var test = @"
using ZibStack.NET.Aop;
using System.Threading;
using System.Threading.Tasks;

public class Svc
{
    [{|#0:Timeout(TimeoutMs = 0)|}]
    public Task<int> GetAsync(CancellationToken ct) => Task.FromResult(1);
}
" + AopStubs;

        var fixedCode = @"
using ZibStack.NET.Aop;
using System.Threading;
using System.Threading.Tasks;

public class Svc
{
    [Timeout(TimeoutMs = 30000)]
    public Task<int> GetAsync(CancellationToken ct) => Task.FromResult(1);
}
" + AopStubs;

        var test1 = new CSharpCodeFixTest<BuiltInAspectArgumentAnalyzer, FixTimeoutValueCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.TimeoutValue)
                .WithLocation(0)
                .WithArguments(0));

        await test1.RunAsync();
    }

    // ── AddRequiredConstructorCodeFix (AOP1004) ────────────────────────────

    [Fact]
    public async Task MissingConstructor_FixedByAddingStub()
    {
        var test = @"
using System;
using ZibStack.NET.Aop;

[RequireConstructor(typeof(IServiceProvider))]
public abstract class Plugin { }

public class {|#0:BrokenPlugin|} : Plugin
{
}
" + AopStubsRequireConstructor;

        var fixedCode = @"
using System;
using ZibStack.NET.Aop;

[RequireConstructor(typeof(IServiceProvider))]
public abstract class Plugin { }

public class BrokenPlugin : Plugin
{
    public BrokenPlugin(System.IServiceProvider p0)
    {
        throw new global::System.NotImplementedException();
    }
}
" + AopStubsRequireConstructor;

        var test1 = new CSharpCodeFixTest<RequireConstructorAnalyzer, AddRequiredConstructorCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.MissingRequiredConstructor)
                .WithLocation(0)
                .WithArguments("BrokenPlugin", "Plugin", "(System.IServiceProvider)", "."));

        await test1.RunAsync();
    }

    [Fact]
    public async Task MissingParameterlessConstructor_FixedByAddingStub()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireConstructor]
public abstract class Activator { }

public class {|#0:NeedsArg|} : Activator
{
    public NeedsArg(int x) { }
}
" + AopStubsRequireConstructor;

        var fixedCode = @"
using ZibStack.NET.Aop;

[RequireConstructor]
public abstract class Activator { }

public class NeedsArg : Activator
{
    public NeedsArg()
    {
        throw new global::System.NotImplementedException();
    }
    public NeedsArg(int x) { }
}
" + AopStubsRequireConstructor;

        var test1 = new CSharpCodeFixTest<RequireConstructorAnalyzer, AddRequiredConstructorCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.MissingRequiredConstructor)
                .WithLocation(0)
                .WithArguments("NeedsArg", "Activator", "()", "."));

        await test1.RunAsync();
    }

    private const string AopStubsRequireConstructor = @"
namespace ZibStack.NET.Aop
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Interface, Inherited = true, AllowMultiple = true)]
    public sealed class RequireConstructorAttribute : System.Attribute
    {
        public System.Type[] ParameterTypes { get; }
        public string? Reason { get; set; }
        public RequireConstructorAttribute(params System.Type[] parameterTypes) { ParameterTypes = parameterTypes; }
    }
}
";

    // TimeoutWithoutCT_FixedByAddingCT was here — removed alongside AOP0015 because
    // TimeoutHandler ignores any CancellationToken anyway, so the fix did nothing useful.

    // ── FixOptionalAspectArgCodeFix (AOP0030–AOP0041) ───────────────────────
    //
    // Single generic code fix covering all 12 Polly + HybridCache numeric-arg
    // diagnostics. Below: one int-default test (PollyRetry MaxRetryAttempts),
    // one double-default test (PollyCircuitBreaker FailureThreshold), and a
    // representative HybridCache test. The other 9 share the same code path.

    private const string PollyHybridCacheStubs = @"
namespace ZibStack.NET.Aop
{
    public class AspectAttribute : System.Attribute { public int Order { get; set; } }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class PollyRetryAttribute : AspectAttribute
    {
        public int MaxRetryAttempts { get; set; } = 3;
        public int DelayMs { get; set; } = 200;
    }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class PollyCircuitBreakerAttribute : AspectAttribute
    {
        public double FailureThreshold { get; set; } = 0.5;
        public int MinimumThroughput { get; set; } = 10;
        public int SamplingDurationSeconds { get; set; } = 30;
        public int BreakDurationSeconds { get; set; } = 15;
    }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class HybridCacheAttribute : AspectAttribute
    {
        public int DurationSeconds { get; set; } = 300;
    }
}
";

    [Fact]
    public async Task PollyRetryMaxAttemptsZero_FixedToThree()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:PollyRetry(MaxRetryAttempts = 0)|}] public int Get() => 1; }
" + PollyHybridCacheStubs;

        var fixedCode = @"
using ZibStack.NET.Aop;
public class Svc { [PollyRetry(MaxRetryAttempts = 3)] public int Get() => 1; }
" + PollyHybridCacheStubs;

        var test1 = new CSharpCodeFixTest<BuiltInAspectArgumentAnalyzer, FixOptionalAspectArgCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.PollyRetryMaxAttempts).WithLocation(0).WithArguments(0));
        await test1.RunAsync();
    }

    [Fact]
    public async Task CircuitBreakerThresholdInvalid_FixedToHalf()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:PollyCircuitBreaker(FailureThreshold = 1.5)|}] public int Get() => 1; }
" + PollyHybridCacheStubs;

        var fixedCode = @"
using ZibStack.NET.Aop;
public class Svc { [PollyCircuitBreaker(FailureThreshold = 0.5)] public int Get() => 1; }
" + PollyHybridCacheStubs;

        var test1 = new CSharpCodeFixTest<BuiltInAspectArgumentAnalyzer, FixOptionalAspectArgCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.PollyCircuitBreakerThreshold).WithLocation(0).WithArguments(1.5));
        await test1.RunAsync();
    }

    [Fact]
    public async Task HybridCacheDurationNegative_FixedToDefault()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:HybridCache(DurationSeconds = -1)|}] public int Get() => 1; }
" + PollyHybridCacheStubs;

        var fixedCode = @"
using ZibStack.NET.Aop;
public class Svc { [HybridCache(DurationSeconds = 300)] public int Get() => 1; }
" + PollyHybridCacheStubs;

        var test1 = new CSharpCodeFixTest<BuiltInAspectArgumentAnalyzer, FixOptionalAspectArgCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.HybridCacheDuration).WithLocation(0).WithArguments(-1));
        await test1.RunAsync();
    }

    [Fact]
    public async Task RateLimiterQueueNegative_FixedToZero()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:PollyRateLimiter(QueueLimit = -5)|}] public int Get() => 1; }

namespace ZibStack.NET.Aop
{
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class PollyRateLimiterAttribute : AspectAttribute
    {
        public int PermitLimit { get; set; } = 100;
        public int WindowSeconds { get; set; } = 60;
        public int QueueLimit { get; set; }
    }
}
" + PollyHybridCacheStubs;

        var fixedCode = @"
using ZibStack.NET.Aop;
public class Svc { [PollyRateLimiter(QueueLimit = 0)] public int Get() => 1; }

namespace ZibStack.NET.Aop
{
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class PollyRateLimiterAttribute : AspectAttribute
    {
        public int PermitLimit { get; set; } = 100;
        public int WindowSeconds { get; set; } = 60;
        public int QueueLimit { get; set; }
    }
}
" + PollyHybridCacheStubs;

        var t = new CSharpCodeFixTest<BuiltInAspectArgumentAnalyzer, FixOptionalAspectArgCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        t.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.PollyRateLimiterQueue).WithLocation(0).WithArguments(-5));
        await t.RunAsync();
    }
}
