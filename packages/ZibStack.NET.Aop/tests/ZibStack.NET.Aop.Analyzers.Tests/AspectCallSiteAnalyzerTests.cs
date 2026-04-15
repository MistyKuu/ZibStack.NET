using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using ZibStack.NET.Aop.Analyzers;

namespace ZibStack.NET.Aop.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<AspectCallSiteAnalyzer, DefaultVerifier>;

public class AspectCallSiteAnalyzerTests
{
    private const string AopStubs = @"
namespace ZibStack.NET.Aop
{
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class, Inherited = true)]
    public class AspectAttribute : System.Attribute { public int Order { get; set; } }

    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class AspectHandlerAttribute : System.Attribute
    {
        public AspectHandlerAttribute(System.Type handlerType) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class, Inherited = true)]
    public sealed class MyAspectAttribute : AspectAttribute { }
}
";

    // ── AOP0020: method group → delegate ──

    [Fact]
    public async Task AspectMethodAsFunc_ReportsAOP0020()
    {
        var test = @"
using ZibStack.NET.Aop;
using System;

public class Svc
{
    [MyAspect]
    public int GetOrder(int id) => id;
}

public class Caller
{
    public Func<int, int> MakeFunc(Svc s) => {|#0:s.GetOrder|};
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.DelegateConversion)
            .WithLocation(0)
            .WithArguments("GetOrder");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AspectMethodInvokedDirectly_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [MyAspect]
    public int GetOrder(int id) => id;
}

public class Caller
{
    public int Run(Svc s) => s.GetOrder(1);
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonAspectMethodAsFunc_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;
using System;

public class Svc
{
    public int GetOrder(int id) => id;
}

public class Caller
{
    public Func<int, int> MakeFunc(Svc s) => s.GetOrder;
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── AOP0021: base.Method() ──

    [Fact]
    public async Task BaseCallToAspectMethod_ReportsAOP0021()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Base
{
    [MyAspect]
    public virtual int GetOrder(int id) => id;
}

public class Derived : Base
{
    public override int GetOrder(int id)
    {
        return {|#0:base.GetOrder(id)|};
    }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.BaseCall)
            .WithLocation(0)
            .WithArguments("GetOrder");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task BaseCallToNonAspectMethod_NoDiagnostic()
    {
        var test = @"
public class Base
{
    public virtual int GetOrder(int id) => id;
}

public class Derived : Base
{
    public override int GetOrder(int id) => base.GetOrder(id);
}
";
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ThisCallToAspectMethod_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [MyAspect]
    public int GetOrder(int id) => id;

    public int Other(int id) => this.GetOrder(id);
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }
}
