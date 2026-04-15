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

    // ── AUDIT: edge cases that could produce false positives or false negatives ──

    [Fact]
    public async Task ScopedType_UsedFromLambda_OutOfScope_Reports()
    {
        // Lambda body referring to the scoped type — analyzer should fire normally
        // (ContainingSymbol resolution walks through the synthesized lambda method
        // to its real ContainingType). Use a separate-statement form so each
        // operation gets its own clean span without overlap.
        var test = @"
using System;
using ZibStack.NET.Aop;

namespace MyApp.Internal
{
    [ScopeTo(""MyApp.Internal.**"")]
    public class SecretEngine { public void DoMagic() { } }
}

namespace MyApp.Public
{
    using MyApp.Internal;
    public class Caller
    {
        public Action MakeLambda() => () =>
        {
            var e = {|#0:new SecretEngine()|};
            {|#1:e.DoMagic()|};
        };
    }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(Diagnostics.OutOfScopeUsage)
                .WithLocation(0)
                .WithArguments("SecretEngine", "'MyApp.Internal.**'", "MyApp.Public", "."),
            Verify.Diagnostic(Diagnostics.OutOfScopeUsage)
                .WithLocation(1)
                .WithArguments("SecretEngine", "'MyApp.Internal.**'", "MyApp.Public", "."));
    }

    [Fact]
    public async Task ScopedType_NameOf_NoDiagnostic()
    {
        // nameof(T) is a string literal at compile time — no actual reference to the
        // type at runtime, no security/scope concern. Should NOT fire.
        var test = @"
using ZibStack.NET.Aop;

namespace MyApp.Internal
{
    [ScopeTo(""MyApp.Internal.**"")]
    public class SecretEngine { }
}

namespace MyApp.Public
{
    using MyApp.Internal;
    public class Caller
    {
        public string GetName() => nameof(SecretEngine);
    }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ScopedType_TypeOf_NoDiagnostic()
    {
        // typeof(T) is a metadata reference (no construction, no member call).
        // Current analyzer scope (ObjectCreation + Invocation) doesn't catch it,
        // and that's intentional — scope rules are about USAGE, not reflection.
        var test = @"
using System;
using ZibStack.NET.Aop;

namespace MyApp.Internal
{
    [ScopeTo(""MyApp.Internal.**"")]
    public class SecretEngine { }
}

namespace MyApp.Public
{
    using MyApp.Internal;
    public class Caller
    {
        public Type Get() => typeof(SecretEngine);
    }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ScopedType_GenericMethodInvocation_OutOfScope_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;

namespace MyApp.Internal
{
    [ScopeTo(""MyApp.Internal.**"")]
    public static class SecretEngine
    {
        public static T Process<T>(T x) => x;
    }
}

namespace MyApp.Public
{
    using MyApp.Internal;
    public class Caller
    {
        public int Use() => {|#0:SecretEngine.Process<int>(5)|};
    }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.OutOfScopeUsage)
            .WithLocation(0)
            .WithArguments("SecretEngine", "'MyApp.Internal.**'", "MyApp.Public", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ScopedType_AsParameterType_NoDiagnostic_NotCovered()
    {
        // Parameter / field / property TYPE references aren't construction or
        // invocation, so the current analyzer doesn't fire for them. Documented
        // as a SCOPE LIMITATION — extending coverage to type references would
        // require IConversionOperation / ITypeOfOperation analysis and would
        // change the analyzer's contract. For now this stays uncovered.
        var test = @"
using ZibStack.NET.Aop;

namespace MyApp.Internal
{
    [ScopeTo(""MyApp.Internal.**"")]
    public class SecretEngine { }
}

namespace MyApp.Public
{
    using MyApp.Internal;
    public class Caller
    {
        public void Use(SecretEngine engine) { }
    }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ScopedType_LinqQuery_OutOfScope_Reports()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;
using ZibStack.NET.Aop;

namespace MyApp.Internal
{
    [ScopeTo(""MyApp.Internal.**"")]
    public class SecretEngine { public int GetValue() => 42; }
}

namespace MyApp.Public
{
    using MyApp.Internal;
    public class Caller
    {
        public IEnumerable<int> Use(IEnumerable<SecretEngine> engines) =>
            engines.Select(e => {|#0:e.GetValue()|});
    }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.OutOfScopeUsage)
            .WithLocation(0)
            .WithArguments("SecretEngine", "'MyApp.Internal.**'", "MyApp.Public", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }
}
