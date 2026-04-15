using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using ZibStack.NET.Aop.Analyzers;
using ZibStack.NET.Aop.CodeFixes;

namespace ZibStack.NET.Aop.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<RequireImplementationAnalyzer, DefaultVerifier>;

public class RequireImplementationAnalyzerTests
{
    private const string AopStubs = @"
namespace ZibStack.NET.Aop
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Interface, Inherited = true, AllowMultiple = true)]
    public sealed class RequireImplementationAttribute : System.Attribute
    {
        public System.Type InterfaceType { get; }
        public string? Reason { get; set; }
        public RequireImplementationAttribute(System.Type interfaceType) { InterfaceType = interfaceType; }
    }
}
";

    [Fact]
    public async Task DerivedMissingInterface_Reports()
    {
        var test = @"
using System;
using ZibStack.NET.Aop;

[RequireImplementation(typeof(IDisposable), Reason = ""Connections must clean up sockets"")]
public abstract class DatabaseConnection { }

public class {|#0:SqlConnection|} : DatabaseConnection { }
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredImplementation)
            .WithLocation(0)
            .WithArguments("SqlConnection", "DatabaseConnection", "IDisposable", ". Reason: Connections must clean up sockets");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task DerivedImplementsInterface_NoDiagnostic()
    {
        var test = @"
using System;
using ZibStack.NET.Aop;

[RequireImplementation(typeof(IDisposable))]
public abstract class DatabaseConnection { }

public class SqlConnection : DatabaseConnection, IDisposable
{
    public void Dispose() { }
}
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task TwoRequirements_BothMissing_ReportsBoth()
    {
        var test = @"
using System;
using ZibStack.NET.Aop;

[RequireImplementation(typeof(IDisposable))]
[RequireImplementation(typeof(IAsyncDisposable))]
public abstract class DatabaseConnection { }

public class {|#0:SqlConnection|} : DatabaseConnection { }
" + AopStubs;

        await Verify.VerifyAnalyzerAsync(test,
            Verify.Diagnostic(Diagnostics.MissingRequiredImplementation)
                .WithLocation(0)
                .WithArguments("SqlConnection", "DatabaseConnection", "IDisposable", "."),
            Verify.Diagnostic(Diagnostics.MissingRequiredImplementation)
                .WithLocation(0)
                .WithArguments("SqlConnection", "DatabaseConnection", "IAsyncDisposable", "."));
    }

    [Fact]
    public async Task AbstractIntermediate_NotReported()
    {
        var test = @"
using System;
using ZibStack.NET.Aop;

[RequireImplementation(typeof(IDisposable))]
public abstract class DatabaseConnection { }

public abstract class PooledConnection : DatabaseConnection { }

public class {|#0:SqlConnection|} : PooledConnection { }
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredImplementation)
            .WithLocation(0)
            .WithArguments("SqlConnection", "DatabaseConnection", "IDisposable", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task InterfaceRequirement_ImplementorWithoutInterface_Reports()
    {
        var test = @"
using System;
using ZibStack.NET.Aop;

[RequireImplementation(typeof(IDisposable))]
public interface IPlugin { }

public class {|#0:SamplePlugin|} : IPlugin { }
" + AopStubs;

        var expected = Verify.Diagnostic(Diagnostics.MissingRequiredImplementation)
            .WithLocation(0)
            .WithArguments("SamplePlugin", "IPlugin", "IDisposable", ".");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    // ── Code fix ──

    [Fact]
    public async Task MissingInterface_FixedByAddingToBaseList()
    {
        var test = @"
using System;
using ZibStack.NET.Aop;

[RequireImplementation(typeof(IDisposable))]
public abstract class DatabaseConnection { }

public class {|#0:SqlConnection|} : DatabaseConnection { }
" + AopStubs;

        // Fixed code adds IDisposable to the base list. CS0535 (member not implemented)
        // would normally fire next — the C# light bulb's own ""Implement interface"" then
        // takes over to stub Dispose(). For this test we only verify the fix's own work.
        var fixedCode = @"
using System;
using ZibStack.NET.Aop;

[RequireImplementation(typeof(IDisposable))]
public abstract class DatabaseConnection { }

public class {|#0:SqlConnection|} : DatabaseConnection, IDisposable { }
" + AopStubs;

        var test1 = new CSharpCodeFixTest<RequireImplementationAnalyzer, AddMissingInterfaceCodeFix, DefaultVerifier>
        {
            TestCode = test,
            FixedCode = fixedCode,
        };
        test1.ExpectedDiagnostics.Add(
            new DiagnosticResult(Diagnostics.MissingRequiredImplementation)
                .WithLocation(0)
                .WithArguments("SqlConnection", "DatabaseConnection", "IDisposable", "."));
        // After the fix, CS0535 for the unimplemented Dispose() will fire — that's
        // expected (and the user's next light-bulb step).
        test1.FixedState.ExpectedDiagnostics.Add(
            DiagnosticResult.CompilerError("CS0535")
                .WithSpan(8, 50, 8, 61)
                .WithArguments("SqlConnection", "System.IDisposable.Dispose()"));
        // The diagnostic on SqlConnection survives in FixedCode markup but the analyzer
        // shouldn't fire there; mark it as also-expected to silence the framework.
        test1.FixedState.MarkupHandling = MarkupMode.Allow;

        await test1.RunAsync();
    }
}
