using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using ZibStack.NET.Aop.Analyzers;

namespace ZibStack.NET.Aop.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<AspectMethodAnalyzer, DefaultVerifier>;

public class AspectMethodAnalyzerTests
{
    // Stubs for the runtime AOP types so the analyzer's symbol lookups (by full-name string)
    // resolve against in-test source. Mirrors how the Log analyzer tests stub ILogger.
    private const string AopStubs = @"
namespace ZibStack.NET.Aop
{
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public class AspectAttribute : System.Attribute
    {
        public int Order { get; set; }
    }

    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class AspectHandlerAttribute : System.Attribute
    {
        public AspectHandlerAttribute(System.Type handlerType) { }
    }

    public class AspectContext
    {
        public string ClassName { get; set; } = """";
        public string MethodName { get; set; } = """";
    }

    public interface IAspectHandler
    {
        void OnBefore(AspectContext context);
        void OnAfter(AspectContext context);
        void OnException(AspectContext context, System.Exception exception);
    }

    [AspectHandler(typeof(MyHandler))]
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class MyAspectAttribute : AspectAttribute { }

    public sealed class MyHandler : IAspectHandler
    {
        public void OnBefore(AspectContext c) { }
        public void OnAfter(AspectContext c) { }
        public void OnException(AspectContext c, System.Exception e) { }
    }
}
";

    // ── AOP0001: static method ──

    [Fact]
    public async Task StaticMethod_WithAspect_ReportsAOP0001()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [MyAspect]
    public static void {|#0:DoWork|}() { }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.StaticMethod)
            .WithLocation(0)
            .WithArguments("MyAspectAttribute", "DoWork");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task InstanceMethod_WithAspect_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [MyAspect]
    public void DoWork() { }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── AOP0002: private/protected ──

    [Fact]
    public async Task PrivateMethod_WithAspect_ReportsAOP0002()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [MyAspect]
    private void {|#0:DoWork|}() { }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.PrivateOrProtected)
            .WithLocation(0)
            .WithArguments("MyAspectAttribute", "DoWork");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ProtectedMethod_WithAspect_ReportsAOP0002()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [MyAspect]
    protected void {|#0:DoWork|}() { }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.PrivateOrProtected)
            .WithLocation(0)
            .WithArguments("MyAspectAttribute", "DoWork");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task InternalMethod_WithAspect_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [MyAspect]
    internal void DoWork() { }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── AOP0003: ref/out/in parameters ──

    [Fact]
    public async Task RefParameter_WithAspect_ReportsAOP0003()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [MyAspect]
    public void {|#0:DoWork|}(ref int x) { x = 1; }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.RefOutInParam)
            .WithLocation(0)
            .WithArguments("MyAspectAttribute", "DoWork");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task OutParameter_WithAspect_ReportsAOP0003()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [MyAspect]
    public void {|#0:DoWork|}(out int x) { x = 1; }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.RefOutInParam)
            .WithLocation(0)
            .WithArguments("MyAspectAttribute", "DoWork");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── AOP0003B: ref returns ──

    [Fact]
    public async Task RefReturn_WithAspect_ReportsAOP0003B()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    private int _x;
    [MyAspect]
    public ref int {|#0:GetRef|}() => ref _x;
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.RefReturn)
            .WithLocation(0)
            .WithArguments("MyAspectAttribute", "GetRef");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── AOP0006: operators / conversions ──

    [Fact]
    public async Task Operator_WithAspect_ReportsAOP0006()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Box
{
    public int Value { get; set; }

    [MyAspect]
    public static Box operator {|#0:+|}(Box a, Box b) => new Box { Value = a.Value + b.Value };
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.OperatorMethod)
            .WithLocation(0)
            .WithArguments("MyAspectAttribute", "op_Addition");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }
}
