using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using ZibStack.NET.Aop.Analyzers;

namespace ZibStack.NET.Aop.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<BuiltInAspectArgumentAnalyzer, DefaultVerifier>;

/// <summary>
/// Tier 2 extensions covering the optional add-on packages
/// (ZibStack.NET.Aop.Polly, ZibStack.NET.Aop.HybridCache). The analyzer is the
/// same <see cref="BuiltInAspectArgumentAnalyzer"/> as core Tier 2 — it just
/// switches on additional attribute full-name strings.
/// </summary>
public class PollyHybridCacheArgumentAnalyzerTests
{
    private const string AopStubs = @"
namespace ZibStack.NET.Aop
{
    public class AspectAttribute : System.Attribute { public int Order { get; set; } }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class PollyRetryAttribute : AspectAttribute
    {
        public int MaxRetryAttempts { get; set; } = 3;
        public int DelayMs { get; set; } = 200;
        public string? PipelineName { get; set; }
    }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class HttpRetryAttribute : AspectAttribute
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
    public sealed class PollyRateLimiterAttribute : AspectAttribute
    {
        public int PermitLimit { get; set; } = 100;
        public int WindowSeconds { get; set; } = 60;
        public int QueueLimit { get; set; }
    }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class HybridCacheAttribute : AspectAttribute
    {
        public int DurationSeconds { get; set; } = 300;
        public string? KeyTemplate { get; set; }
    }
}
";

    // ── PollyRetry ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PollyRetry_MaxAttemptsZero_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:PollyRetry(MaxRetryAttempts = 0)|}] public int Get() => 1; }
" + AopStubs;
        var expected = Verify.Diagnostic(Diagnostics.PollyRetryMaxAttempts).WithLocation(0).WithArguments(0);
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task PollyRetry_DelayNegative_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:PollyRetry(DelayMs = -100)|}] public int Get() => 1; }
" + AopStubs;
        var expected = Verify.Diagnostic(Diagnostics.PollyRetryDelay).WithLocation(0).WithArguments(-100);
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task PollyRetry_Defaults_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [PollyRetry] public int Get() => 1; }
" + AopStubs;
        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── HttpRetry ───────────────────────────────────────────────────────────

    [Fact]
    public async Task HttpRetry_MaxAttemptsZero_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:HttpRetry(MaxRetryAttempts = 0)|}] public int Get() => 1; }
" + AopStubs;
        var expected = Verify.Diagnostic(Diagnostics.HttpRetryMaxAttempts).WithLocation(0).WithArguments(0);
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task HttpRetry_DelayNegative_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:HttpRetry(DelayMs = -1)|}] public int Get() => 1; }
" + AopStubs;
        var expected = Verify.Diagnostic(Diagnostics.HttpRetryDelay).WithLocation(0).WithArguments(-1);
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── PollyCircuitBreaker ─────────────────────────────────────────────────

    [Fact]
    public async Task CircuitBreaker_FailureThresholdAboveOne_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:PollyCircuitBreaker(FailureThreshold = 1.5)|}] public int Get() => 1; }
" + AopStubs;
        var expected = Verify.Diagnostic(Diagnostics.PollyCircuitBreakerThreshold).WithLocation(0).WithArguments(1.5);
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CircuitBreaker_FailureThresholdZero_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:PollyCircuitBreaker(FailureThreshold = 0)|}] public int Get() => 1; }
" + AopStubs;
        var expected = Verify.Diagnostic(Diagnostics.PollyCircuitBreakerThreshold).WithLocation(0).WithArguments(0d);
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CircuitBreaker_FailureThresholdInRange_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [PollyCircuitBreaker(FailureThreshold = 0.7)] public int Get() => 1; }
" + AopStubs;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CircuitBreaker_MinimumThroughputZero_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:PollyCircuitBreaker(MinimumThroughput = 0)|}] public int Get() => 1; }
" + AopStubs;
        var expected = Verify.Diagnostic(Diagnostics.PollyCircuitBreakerThroughput).WithLocation(0).WithArguments(0);
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CircuitBreaker_SamplingZero_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:PollyCircuitBreaker(SamplingDurationSeconds = 0)|}] public int Get() => 1; }
" + AopStubs;
        var expected = Verify.Diagnostic(Diagnostics.PollyCircuitBreakerSampling).WithLocation(0).WithArguments(0);
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CircuitBreaker_BreakZero_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:PollyCircuitBreaker(BreakDurationSeconds = 0)|}] public int Get() => 1; }
" + AopStubs;
        var expected = Verify.Diagnostic(Diagnostics.PollyCircuitBreakerBreak).WithLocation(0).WithArguments(0);
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── PollyRateLimiter ────────────────────────────────────────────────────

    [Fact]
    public async Task RateLimiter_PermitsZero_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:PollyRateLimiter(PermitLimit = 0)|}] public int Get() => 1; }
" + AopStubs;
        var expected = Verify.Diagnostic(Diagnostics.PollyRateLimiterPermits).WithLocation(0).WithArguments(0);
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task RateLimiter_WindowZero_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:PollyRateLimiter(WindowSeconds = 0)|}] public int Get() => 1; }
" + AopStubs;
        var expected = Verify.Diagnostic(Diagnostics.PollyRateLimiterWindow).WithLocation(0).WithArguments(0);
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task RateLimiter_QueueNegative_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:PollyRateLimiter(QueueLimit = -5)|}] public int Get() => 1; }
" + AopStubs;
        var expected = Verify.Diagnostic(Diagnostics.PollyRateLimiterQueue).WithLocation(0).WithArguments(-5);
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── HybridCache ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HybridCache_DurationNegative_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [{|#0:HybridCache(DurationSeconds = -1)|}] public int Get() => 1; }
" + AopStubs;
        var expected = Verify.Diagnostic(Diagnostics.HybridCacheDuration).WithLocation(0).WithArguments(-1);
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task HybridCache_DurationZero_NoDiagnostic_AsForever()
    {
        // 0 = unlimited TTL by package convention — must not fire.
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [HybridCache(DurationSeconds = 0)] public int Get() => 1; }
" + AopStubs;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HybridCache_Defaults_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;
public class Svc { [HybridCache] public int Get() => 1; }
" + AopStubs;
        await Verify.VerifyAnalyzerAsync(test);
    }
}
