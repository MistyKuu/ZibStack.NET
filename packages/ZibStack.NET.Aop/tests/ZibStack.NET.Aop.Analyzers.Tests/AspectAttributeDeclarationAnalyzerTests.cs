using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using ZibStack.NET.Aop.Analyzers;

namespace ZibStack.NET.Aop.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<AspectAttributeDeclarationAnalyzer, DefaultVerifier>;

public class AspectAttributeDeclarationAnalyzerTests
{
    private const string AopStubs = @"
namespace ZibStack.NET.Aop
{
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public class AspectAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class AspectHandlerAttribute : System.Attribute
    {
        public AspectHandlerAttribute(System.Type handlerType) { }
    }

    public class AspectContext { }

    public interface IAspectHandler
    {
        void OnBefore(AspectContext context);
        void OnAfter(AspectContext context);
        void OnException(AspectContext context, System.Exception exception);
    }

    public interface IAroundAspectHandler
    {
        object? Around(AspectContext context, System.Func<object?> proceed);
    }
}
";

    // ── AOP0004: handler doesn't implement any handler interface ──

    [Fact]
    public async Task HandlerNotImplementingInterface_ReportsAOP0004()
    {
        var test = @"
using ZibStack.NET.Aop;

[{|#0:AspectHandler(typeof(NotAHandler))|}]
[System.AttributeUsage(System.AttributeTargets.Method)]
public sealed class BrokenAspectAttribute : AspectAttribute { }

public class NotAHandler { }
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.HandlerTypeMismatch)
            .WithLocation(0)
            .WithArguments("NotAHandler");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task HandlerImplementingIAspectHandler_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

[AspectHandler(typeof(GoodHandler))]
[System.AttributeUsage(System.AttributeTargets.Method)]
public sealed class GoodAspectAttribute : AspectAttribute { }

public sealed class GoodHandler : IAspectHandler
{
    public void OnBefore(AspectContext c) { }
    public void OnAfter(AspectContext c) { }
    public void OnException(AspectContext c, System.Exception e) { }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HandlerImplementingIAroundAspectHandler_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

[AspectHandler(typeof(AroundHandler))]
[System.AttributeUsage(System.AttributeTargets.Method)]
public sealed class GoodAroundAttribute : AspectAttribute { }

public sealed class AroundHandler : IAroundAspectHandler
{
    public object? Around(AspectContext c, System.Func<object?> p) => p();
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── AOP0005: aspect attribute with no [AspectHandler] ──

    [Fact]
    public async Task AspectAttributeWithoutHandlerAttribute_ReportsAOP0005()
    {
        var test = @"
using ZibStack.NET.Aop;

[System.AttributeUsage(System.AttributeTargets.Method)]
public sealed class {|#0:OrphanedAspectAttribute|} : AspectAttribute { }
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingHandlerAttribute)
            .WithLocation(0)
            .WithArguments("OrphanedAspectAttribute");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── Abstract aspect base classes are exempt ──

    [Fact]
    public async Task AbstractAspectAttribute_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

[System.AttributeUsage(System.AttributeTargets.Method)]
public abstract class AbstractAspectAttribute : AspectAttribute { }
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }
}
