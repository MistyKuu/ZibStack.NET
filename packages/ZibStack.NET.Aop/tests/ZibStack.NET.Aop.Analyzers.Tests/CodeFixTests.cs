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
    public sealed class RetryAttribute : AspectAttribute
    {
        public int MaxAttempts { get; set; } = 3;
    }

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
}
