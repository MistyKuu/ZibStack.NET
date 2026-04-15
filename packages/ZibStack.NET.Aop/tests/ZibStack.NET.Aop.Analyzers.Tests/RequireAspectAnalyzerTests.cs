using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using ZibStack.NET.Aop.Analyzers;
using ZibStack.NET.Aop.CodeFixes;

namespace ZibStack.NET.Aop.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<RequireAspectAnalyzer, DefaultVerifier>;

public class RequireAspectAnalyzerTests
{
    private const string AopStubs = @"
namespace ZibStack.NET.Aop
{
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class, Inherited = true)]
    public class AspectAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Interface, Inherited = true, AllowMultiple = true)]
    public sealed class RequireAspectAttribute : System.Attribute
    {
        public System.Type AspectType { get; }
        public string? Reason { get; set; }
        public RequireAspectAttribute(System.Type aspectType) { AspectType = aspectType; }
    }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class, Inherited = true)]
    public sealed class LogAttribute : AspectAttribute { }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class, Inherited = true)]
    public sealed class TraceAttribute : AspectAttribute { }
}
";

    // ── Direct base class missing aspect ──

    [Fact]
    public async Task DerivedMissingAspect_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireAspect(typeof(LogAttribute), Reason = ""All Topics must be audited"")]
public abstract class Topic { }

public class {|#0:OrderPlaced|} : Topic { }
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredAspect)
            .WithLocation(0)
            .WithArguments("OrderPlaced", "Topic", "Log", ". Reason: All Topics must be audited");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DerivedWithAspect_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireAspect(typeof(LogAttribute))]
public abstract class Topic { }

[Log]
public class OrderPlaced : Topic { }
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── Interface requirement ──

    [Fact]
    public async Task InterfaceImplementorMissingAspect_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireAspect(typeof(TraceAttribute))]
public interface ICommandHandler { }

public class {|#0:CreateOrderHandler|} : ICommandHandler { }
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredAspect)
            .WithLocation(0)
            .WithArguments("CreateOrderHandler", "ICommandHandler", "Trace", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── Abstract derivative is exempt ──

    [Fact]
    public async Task AbstractIntermediate_NotReported()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireAspect(typeof(LogAttribute))]
public abstract class Topic { }

// Abstract intermediates aren't concrete usage sites — skip them.
public abstract class CategoryTopic : Topic { }

public class {|#0:OrderPlaced|} : CategoryTopic { }
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredAspect)
            .WithLocation(0)
            .WithArguments("OrderPlaced", "Topic", "Log", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── Multiple requirements ──

    [Fact]
    public async Task TwoRequirements_BothMissing_ReportsBoth()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireAspect(typeof(LogAttribute))]
[RequireAspect(typeof(TraceAttribute))]
public abstract class Topic { }

public class {|#0:OrderPlaced|} : Topic { }
" + AopStubs;

        // Two diagnostics — same location, different aspect names.
        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(Diagnostics.MissingRequiredAspect)
                .WithLocation(0)
                .WithArguments("OrderPlaced", "Topic", "Log", "."),
            Verify.Diagnostic(Diagnostics.MissingRequiredAspect)
                .WithLocation(0)
                .WithArguments("OrderPlaced", "Topic", "Trace", "."));
    }

    [Fact]
    public async Task TwoRequirements_OneSatisfied_OnlyOtherReported()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireAspect(typeof(LogAttribute))]
[RequireAspect(typeof(TraceAttribute))]
public abstract class Topic { }

[Log]
public class {|#0:OrderPlaced|} : Topic { }
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredAspect)
            .WithLocation(0)
            .WithArguments("OrderPlaced", "Topic", "Trace", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── Same requirement from base + interface — collapsed to one diagnostic ──

    [Fact]
    public async Task SameRequirementFromBaseAndInterface_ReportsOnce()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireAspect(typeof(LogAttribute))]
public abstract class Topic { }

[RequireAspect(typeof(LogAttribute))]
public interface IAuditable { }

public class {|#0:OrderPlaced|} : Topic, IAuditable { }
" + AopStubs;

        // Base wins — first diagnostic args reflect the base's source name.
        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredAspect)
            .WithLocation(0)
            .WithArguments("OrderPlaced", "Topic", "Log", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── Type without any [RequireAspect] in chain ──

    [Fact]
    public async Task UnrelatedType_NoDiagnostic()
    {
        var test = @"
public class StandaloneClass { }

public class Derived : StandaloneClass { }
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── Code fix ──

    [Fact]
    public async Task MissingAspect_FixedByAddingAttribute()
    {
        var test = @"
using ZibStack.NET.Aop;

[RequireAspect(typeof(LogAttribute))]
public abstract class Topic { }

public class {|#0:OrderPlaced|} : Topic { }
" + AopStubs;

        var fixedCode = @"
using ZibStack.NET.Aop;

[RequireAspect(typeof(LogAttribute))]
public abstract class Topic { }

[Log]
public class OrderPlaced : Topic { }
" + AopStubs;

        var test1 = new CSharpCodeFixTest<RequireAspectAnalyzer, AddMissingAspectCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.MissingRequiredAspect)
                .WithLocation(0)
                .WithArguments("OrderPlaced", "Topic", "Log", "."));

        await test1.RunAsync();
    }
}
