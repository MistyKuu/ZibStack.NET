using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using ZibStack.NET.Aop.Analyzers;

namespace ZibStack.NET.Aop.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<BuiltInAspectArgumentAnalyzer, DefaultVerifier>;

public class BuiltInAspectArgumentAnalyzerTests
{
    // Stubs covering all four built-in aspects + DataAnnotations marker so the
    // analyzer's full-name lookups resolve.
    private const string AopStubs = @"
namespace ZibStack.NET.Aop
{
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
}

namespace System.ComponentModel.DataAnnotations
{
    public abstract class ValidationAttribute : System.Attribute { }
    public sealed class RequiredAttribute : ValidationAttribute { }
    public sealed class RangeAttribute : ValidationAttribute
    {
        public RangeAttribute(int min, int max) { }
    }
}
";

    // ── AOP0010: [Cache] on void / non-generic Task ──

    [Fact]
    public async Task CacheOnVoidMethod_ReportsAOP0010()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [{|#0:Cache|}]
    public void DoWork() { }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.CacheNonReturning)
            .WithLocation(0)
            .WithArguments("DoWork");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CacheOnTaskMethod_ReportsAOP0010()
    {
        var test = @"
using ZibStack.NET.Aop;
using System.Threading.Tasks;

public class Svc
{
    [{|#0:Cache|}]
    public Task DoWorkAsync() => Task.CompletedTask;
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.CacheNonReturning)
            .WithLocation(0)
            .WithArguments("DoWorkAsync");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task CacheOnGenericTaskMethod_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;
using System.Threading.Tasks;

public class Svc
{
    [Cache]
    public Task<int> GetAsync() => Task.FromResult(1);
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task CacheOnReturningMethod_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [Cache]
    public int Get(int id) => id;
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── AOP0011: [Retry] MaxAttempts ──

    [Fact]
    public async Task RetryMaxAttemptsZero_ReportsAOP0011()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [{|#0:Retry(MaxAttempts = 0)|}]
    public int Get() => 1;
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.RetryMaxAttempts)
            .WithLocation(0)
            .WithArguments(0);

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task RetryMaxAttemptsNegative_ReportsAOP0011()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [{|#0:Retry(MaxAttempts = -3)|}]
    public int Get() => 1;
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.RetryMaxAttempts)
            .WithLocation(0)
            .WithArguments(-3);

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task RetryDefault_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [Retry]
    public int Get() => 1;
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── AOP0012: [Retry] DelayMs ──

    [Fact]
    public async Task RetryDelayNegative_ReportsAOP0012()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [{|#0:Retry(DelayMs = -100)|}]
    public int Get() => 1;
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.RetryDelay)
            .WithLocation(0)
            .WithArguments(-100);

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── AOP0013: [Retry] BackoffMultiplier < 1.0 ──

    [Fact]
    public async Task RetryBackoffShrinking_ReportsAOP0013()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [{|#0:Retry(BackoffMultiplier = 0.5)|}]
    public int Get() => 1;
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.RetryBackoff)
            .WithLocation(0)
            .WithArguments(0.5);

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── AOP0014: [Timeout] TimeoutMs ──

    [Fact]
    public async Task TimeoutZero_ReportsAOP0014()
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

        var expected = Verify.Diagnostic(Diagnostics.TimeoutValue)
            .WithLocation(0)
            .WithArguments(0);

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── AOP0015: [Timeout] without CancellationToken parameter ──

    [Fact]
    public async Task TimeoutWithoutCancellationToken_ReportsAOP0015()
    {
        var test = @"
using ZibStack.NET.Aop;
using System.Threading.Tasks;

public class Svc
{
    [{|#0:Timeout(TimeoutMs = 5000)|}]
    public Task<int> GetAsync() => Task.FromResult(1);
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.TimeoutNoCancellationToken)
            .WithLocation(0)
            .WithArguments("GetAsync");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TimeoutWithCancellationToken_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;
using System.Threading;
using System.Threading.Tasks;

public class Svc
{
    [Timeout(TimeoutMs = 5000)]
    public Task<int> GetAsync(CancellationToken ct) => Task.FromResult(1);
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── AOP0016: [Validate] on parameterless method ──

    [Fact]
    public async Task ValidateOnParameterlessMethod_ReportsAOP0016()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [{|#0:Validate|}]
    public int Get() => 1;
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.ValidateNoParameters)
            .WithLocation(0)
            .WithArguments("Get");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── AOP0017: [Validate] params with no DataAnnotations ──

    [Fact]
    public async Task ValidateOnParamsWithoutAnnotations_ReportsAOP0017()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [{|#0:Validate|}]
    public int Sum(int a, int b) => a + b;
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.ValidateNoAnnotations)
            .WithLocation(0)
            .WithArguments("Sum");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ValidateOnParamsWithDirectAnnotation_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;
using System.ComponentModel.DataAnnotations;

public class Svc
{
    [Validate]
    public string Echo([Required] string s) => s;
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ValidateOnComplexParamWithAnnotatedProperty_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;
using System.ComponentModel.DataAnnotations;

public class Order
{
    [Range(1, 100)]
    public int Quantity { get; set; }
}

public class Svc
{
    [Validate]
    public void Place(Order order) { }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }
}
