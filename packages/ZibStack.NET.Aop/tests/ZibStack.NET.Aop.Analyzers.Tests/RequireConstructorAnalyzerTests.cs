using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using ZibStack.NET.Aop.Analyzers;

namespace ZibStack.NET.Aop.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<RequireConstructorAnalyzer, DefaultVerifier>;

public class RequireConstructorAnalyzerTests
{
    private const string AopStubs = @"
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

    [Fact]
    public async Task DerivedMissingCtor_Reports()
    {
        var test = @"
using System;
using ZibStack.NET.Aop;

[RequireConstructor(typeof(IServiceProvider), Reason = ""Plugins are activated by the host"")]
public abstract class Plugin { }

public class {|#0:BrokenPlugin|} : Plugin
{
    public BrokenPlugin() { }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredConstructor)
            .WithLocation(0)
            .WithArguments("BrokenPlugin", "Plugin", "(System.IServiceProvider)", ". Reason: Plugins are activated by the host");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DerivedHasMatchingCtor_NoDiagnostic()
    {
        var test = @"
using System;
using ZibStack.NET.Aop;

[RequireConstructor(typeof(IServiceProvider))]
public abstract class Plugin { }

public class GoodPlugin : Plugin
{
    public GoodPlugin(IServiceProvider sp) { }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ParameterlessRequirement_DerivedHasOnlyParameterized_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireConstructor]   // empty params = require parameterless ctor
public abstract class Activator { }

public class {|#0:NeedsArg|} : Activator
{
    public NeedsArg(int x) { }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredConstructor)
            .WithLocation(0)
            .WithArguments("NeedsArg", "Activator", "()", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ParameterlessRequirement_ImplicitDefaultCtor_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireConstructor]
public abstract class Activator { }

public class FineByMe : Activator { }   // implicit public Foo() {}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonPublicCtor_DoesNotSatisfy()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireConstructor]
public abstract class Activator { }

public class {|#0:HiddenCtor|} : Activator
{
    private HiddenCtor() { }   // private ctor doesn't satisfy a public-ctor rule
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredConstructor)
            .WithLocation(0)
            .WithArguments("HiddenCtor", "Activator", "()", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task TwoRequirements_OneSatisfied_OnlyOtherReported()
    {
        var test = @"
using System;
using ZibStack.NET.Aop;

[RequireConstructor(typeof(IServiceProvider))]
[RequireConstructor(typeof(int), typeof(string))]
public abstract class Plugin { }

public class {|#0:HalfGood|} : Plugin
{
    public HalfGood(IServiceProvider sp) { }
    // missing the (int, string) shape
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredConstructor)
            .WithLocation(0)
            .WithArguments("HalfGood", "Plugin", "(int, string)", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AbstractIntermediate_NotReported()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireConstructor]
public abstract class Activator { }

public abstract class CategoryActivator : Activator { }

public class {|#0:Concrete|} : CategoryActivator
{
    public Concrete(int x) { }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredConstructor)
            .WithLocation(0)
            .WithArguments("Concrete", "Activator", "()", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }
}
