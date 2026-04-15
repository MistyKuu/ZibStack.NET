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

    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Interface | System.AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
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

    // ── Method-level: interface implementation ──

    [Fact]
    public async Task InterfaceMethod_ImplWithoutAspect_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;
using System.Threading.Tasks;

public interface ICommandHandler
{
    [RequireAspect(typeof(TraceAttribute), Reason = ""All command handlers must be traceable"")]
    Task HandleAsync(object cmd);
}

public class CreateOrderHandler : ICommandHandler
{
    public Task {|#0:HandleAsync|}(object cmd) => Task.CompletedTask;
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredAspect)
            .WithLocation(0)
            .WithArguments("HandleAsync", "ICommandHandler.HandleAsync", "Trace", ". Reason: All command handlers must be traceable");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task InterfaceMethod_ImplWithAspect_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;
using System.Threading.Tasks;

public interface ICommandHandler
{
    [RequireAspect(typeof(TraceAttribute))]
    Task HandleAsync(object cmd);
}

public class CreateOrderHandler : ICommandHandler
{
    [Trace]
    public Task HandleAsync(object cmd) => Task.CompletedTask;
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── Class-level aspect satisfies method-level requirement ──

    [Fact]
    public async Task ClassLevelAspect_SatisfiesMethodLevelRequirement_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;
using System.Threading.Tasks;

public interface ICommandHandler
{
    [RequireAspect(typeof(TraceAttribute))]
    Task HandleAsync(object cmd);
}

[Trace]
public class CreateOrderHandler : ICommandHandler
{
    public Task HandleAsync(object cmd) => Task.CompletedTask;
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── Method-level: abstract override ──

    [Fact]
    public async Task AbstractMethod_OverrideWithoutAspect_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;

public abstract class Topic
{
    [RequireAspect(typeof(LogAttribute))]
    public abstract void Persist();
}

public class OrderPlaced : Topic
{
    public override void {|#0:Persist|}() { }
}
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredAspect)
            .WithLocation(0)
            .WithArguments("Persist", "Topic.Persist", "Log", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task AbstractMethod_OverrideWithAspect_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

public abstract class Topic
{
    [RequireAspect(typeof(LogAttribute))]
    public abstract void Persist();
}

public class OrderPlaced : Topic
{
    [Log]
    public override void Persist() { }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── Method-level: only the implementing method, not other methods on impl ──

    [Fact]
    public async Task InterfaceMethod_OtherMethodsNotFlagged()
    {
        var test = @"
using ZibStack.NET.Aop;
using System.Threading.Tasks;

public interface ICommandHandler
{
    [RequireAspect(typeof(TraceAttribute))]
    Task HandleAsync(object cmd);
}

public class CreateOrderHandler : ICommandHandler
{
    [Trace]
    public Task HandleAsync(object cmd) => Task.CompletedTask;

    public void HelperMethod() { }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── Method-level + class-level rule together ──

    [Fact]
    public async Task ClassAndMethodRules_BothMissing_ReportsBoth()
    {
        var test = @"
using ZibStack.NET.Aop;
using System.Threading.Tasks;

[RequireAspect(typeof(TraceAttribute))]
public interface ICommandHandler
{
    [RequireAspect(typeof(LogAttribute))]
    Task HandleAsync(object cmd);
}

public class {|#0:CreateOrderHandler|} : ICommandHandler
{
    public Task {|#1:HandleAsync|}(object cmd) => Task.CompletedTask;
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(Diagnostics.MissingRequiredAspect)
                .WithLocation(0)
                .WithArguments("CreateOrderHandler", "ICommandHandler", "Trace", "."),
            Verify.Diagnostic(Diagnostics.MissingRequiredAspect)
                .WithLocation(1)
                .WithArguments("HandleAsync", "ICommandHandler.HandleAsync", "Log", "."));
    }

    // ── Method-level code fix ──

    [Fact]
    public async Task MethodMissingAspect_FixedByAddingAttribute()
    {
        var test = @"
using ZibStack.NET.Aop;
using System.Threading.Tasks;

public interface ICommandHandler
{
    [RequireAspect(typeof(TraceAttribute))]
    Task HandleAsync(object cmd);
}

public class CreateOrderHandler : ICommandHandler
{
    public Task {|#0:HandleAsync|}(object cmd) => Task.CompletedTask;
}
" + AopStubs;

        var fixedCode = @"
using ZibStack.NET.Aop;
using System.Threading.Tasks;

public interface ICommandHandler
{
    [RequireAspect(typeof(TraceAttribute))]
    Task HandleAsync(object cmd);
}

public class CreateOrderHandler : ICommandHandler
{
    [Trace]
    public Task HandleAsync(object cmd) => Task.CompletedTask;
}
" + AopStubs;

        var test1 = new CSharpCodeFixTest<RequireAspectAnalyzer, AddMissingAspectCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.MissingRequiredAspect)
                .WithLocation(0)
                .WithArguments("HandleAsync", "ICommandHandler.HandleAsync", "Trace", "."));

        await test1.RunAsync();
    }

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
