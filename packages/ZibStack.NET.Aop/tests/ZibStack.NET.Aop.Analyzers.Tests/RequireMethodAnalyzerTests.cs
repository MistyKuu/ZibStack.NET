using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using ZibStack.NET.Aop.Analyzers;

namespace ZibStack.NET.Aop.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<RequireMethodAnalyzer, DefaultVerifier>;

public class RequireMethodAnalyzerTests
{
    private const string AopStubs = @"
namespace ZibStack.NET.Aop
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Interface, Inherited = true, AllowMultiple = true)]
    public sealed class RequireMethodAttribute : System.Attribute
    {
        public string Name { get; }
        public System.Type? ReturnType { get; set; }
        public System.Type[]? Parameters { get; set; }
        public string? Reason { get; set; }
        public RequireMethodAttribute(string name) { Name = name; }
    }
}
";

    // ── Name-only check ──

    [Fact]
    public async Task DerivedMissingNamedMethod_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireMethod(""Configure"", Reason = ""Modules must register their services"")]
public abstract class Module { }

public class {|#0:AuthModule|} : Module { }
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredMethod)
            .WithLocation(0)
            .WithArguments("AuthModule", "Module", "Configure(...)", ". Reason: Modules must register their services");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DerivedHasNamedMethod_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireMethod(""Configure"")]
public abstract class Module { }

public class AuthModule : Module
{
    public void Configure() { }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── Return type check ──

    [Fact]
    public async Task ReturnTypeMismatch_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireMethod(""Run"", ReturnType = typeof(int))]
public abstract class Worker { }

public class {|#0:LogWorker|} : Worker
{
    public void Run() { }   // wrong return type
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredMethod)
            .WithLocation(0)
            .WithArguments("LogWorker", "Worker", "int Run(...)", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ReturnTypeMatch_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireMethod(""Run"", ReturnType = typeof(int))]
public abstract class Worker { }

public class LogWorker : Worker
{
    public int Run() => 0;
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── Parameter type check ──

    [Fact]
    public async Task ParameterMismatch_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireMethod(""Configure"", ReturnType = typeof(void), Parameters = new[] { typeof(int) })]
public abstract class Module { }

public class {|#0:AuthModule|} : Module
{
    public void Configure(string s) { }   // wrong param type
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredMethod)
            .WithLocation(0)
            .WithArguments("AuthModule", "Module", "void Configure(int)", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ParameterMatch_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireMethod(""Configure"", ReturnType = typeof(void), Parameters = new[] { typeof(int) })]
public abstract class Module { }

public class AuthModule : Module
{
    public void Configure(int x) { }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── Inherited from base satisfies ──

    [Fact]
    public async Task MethodInheritedFromBase_Satisfies()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireMethod(""Configure"")]
public abstract class Module
{
    public virtual void Configure() { }   // base provides default impl
}

public class AuthModule : Module { }
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── Multiple requirements ──

    [Fact]
    public async Task TwoRequirements_OneSatisfied_OnlyOtherReported()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireMethod(""Configure"")]
[RequireMethod(""Initialize"")]
public abstract class Module { }

public class {|#0:AuthModule|} : Module
{
    public void Configure() { }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredMethod)
            .WithLocation(0)
            .WithArguments("AuthModule", "Module", "Initialize(...)", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }
}
