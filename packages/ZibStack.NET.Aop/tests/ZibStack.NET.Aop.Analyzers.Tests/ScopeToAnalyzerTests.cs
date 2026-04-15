using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using ZibStack.NET.Aop.Analyzers;

namespace ZibStack.NET.Aop.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<ScopeToAnalyzer, DefaultVerifier>;

public class ScopeToAnalyzerTests
{
    private const string AopStubs = @"
namespace ZibStack.NET.Aop
{
    [System.AttributeUsage(
        System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Interface | System.AttributeTargets.Method,
        Inherited = false, AllowMultiple = true)]
    public sealed class ScopeToAttribute : System.Attribute
    {
        public string Namespace { get; }
        public string? Reason { get; set; }
        public ScopeToAttribute(string @namespace) { Namespace = @namespace; }
    }
}
";

    // ── Object creation outside scope ──

    [Fact]
    public async Task NewOutsideScope_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;

namespace MyApp.Internal
{
    [ScopeTo(""MyApp.Internal.**"", Reason = ""Engine bypass"")]
    public class SecretEngine
    {
        public void DoMagic() { }
    }
}

namespace MyApp.Public
{
    using MyApp.Internal;
    public class Caller
    {
        public void Use() { var e = {|#0:new SecretEngine()|}; }
    }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.OutOfScopeUsage)
            .WithLocation(0)
            .WithArguments("SecretEngine", "'MyApp.Internal.**'", "MyApp.Public", ". Reason: Engine bypass");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NewInsideScope_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

namespace MyApp.Internal
{
    [ScopeTo(""MyApp.Internal.**"")]
    public class SecretEngine { }
}

namespace MyApp.Internal.Things
{
    using MyApp.Internal;
    public class InsiderCaller
    {
        public void Use() { var e = new SecretEngine(); }
    }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NewInsideExactNamespace_NoDiagnostic_WithoutWildcard()
    {
        var test = @"
using ZibStack.NET.Aop;

namespace MyApp.Internal
{
    [ScopeTo(""MyApp.Internal"")]
    public class SecretEngine { }

    public class InsiderCaller
    {
        public void Use() { var e = new SecretEngine(); }
    }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ExactNamespace_DoesNotMatchSubnamespace()
    {
        var test = @"
using ZibStack.NET.Aop;

namespace MyApp.Internal
{
    [ScopeTo(""MyApp.Internal"")]
    public class SecretEngine { }
}

namespace MyApp.Internal.Things
{
    using MyApp.Internal;
    public class Caller
    {
        public void Use() { var e = {|#0:new SecretEngine()|}; }
    }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.OutOfScopeUsage)
            .WithLocation(0)
            .WithArguments("SecretEngine", "'MyApp.Internal'", "MyApp.Internal.Things", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── Method invocation ──

    [Fact]
    public async Task MethodCallOutsideScope_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;

namespace MyApp.Internal
{
    [ScopeTo(""MyApp.Internal.**"")]
    public class SecretEngine
    {
        public static int Compute(int x) => x * 2;
    }
}

namespace MyApp.Public
{
    using MyApp.Internal;
    public class Caller
    {
        public int Use() => {|#0:SecretEngine.Compute(5)|};
    }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.OutOfScopeUsage)
            .WithLocation(0)
            .WithArguments("SecretEngine", "'MyApp.Internal.**'", "MyApp.Public", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── Multiple ScopeTo allows any of them ──

    [Fact]
    public async Task MultipleScopeRules_AnyMatchSatisfies()
    {
        var test = @"
using ZibStack.NET.Aop;

namespace MyApp.Internal
{
    [ScopeTo(""MyApp.Internal.**"")]
    [ScopeTo(""MyApp.Tests.**"")]
    public class SecretEngine { }
}

namespace MyApp.Tests.Unit
{
    using MyApp.Internal;
    public class TestCaller
    {
        public void Use() { var e = new SecretEngine(); }   // satisfied by Tests.** rule
    }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── Self-reference inside the type itself is always allowed ──

    [Fact]
    public async Task SelfReferenceInsideType_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

namespace MyApp.Internal
{
    [ScopeTo(""MyApp.Internal.**"")]
    public class SecretEngine
    {
        public static SecretEngine Instance() => new SecretEngine();   // self-call inside engine
    }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── Type without [ScopeTo] is unrestricted ──

    [Fact]
    public async Task UnrestrictedType_NoDiagnostic()
    {
        var test = @"
namespace MyApp.Internal
{
    public class Helper { }
}

namespace MyApp.Public
{
    using MyApp.Internal;
    public class Caller
    {
        public void Use() { var h = new Helper(); }
    }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }
}
