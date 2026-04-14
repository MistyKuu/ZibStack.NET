using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using ZibStack.NET.Log.Analyzers;

namespace ZibStack.NET.Log.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<LogAttributeValidityAnalyzer, DefaultVerifier>;

public class LogAttributeValidityAnalyzerTests
{
    // The analyzer matches by FQN string so this stub is enough to make `[Log]` resolve
    // in the test compilation without pulling in the real abstractions package.
    private const string LogAttributeStub = @"
namespace ZibStack.NET.Log
{
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class LogAttribute : System.Attribute { }
}
";

    [Fact]
    public async Task PrivateMethod_WithLog_Reports()
    {
        var test = @"
using ZibStack.NET.Log;

public class Svc
{
    [Log]
    private int {|#0:Internal|}(int x) => x;
}
" + LogAttributeStub;

        var expected = Verify.Diagnostic(LogAttributeValidityAnalyzer.PrivateMemberId)
            .WithLocation(0)
            .WithArguments("Internal");
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task PublicMethod_WithLog_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Log;

public class Svc
{
    [Log]
    public int Get(int x) => x;
}
" + LogAttributeStub;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task RefReturnMethod_WithLog_Reports()
    {
        var test = @"
using ZibStack.NET.Log;

public class Svc
{
    private int _value;
    [Log]
    public ref int {|#0:GetRef|}() => ref _value;
}
" + LogAttributeStub;

        var expected = Verify.Diagnostic(LogAttributeValidityAnalyzer.RefReturnId)
            .WithLocation(0)
            .WithArguments("GetRef");
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task RefReadonlyReturnMethod_WithLog_Reports()
    {
        var test = @"
using ZibStack.NET.Log;

public class Svc
{
    private int _value;
    [Log]
    public ref readonly int {|#0:GetRef|}() => ref _value;
}
" + LogAttributeStub;

        var expected = Verify.Diagnostic(LogAttributeValidityAnalyzer.RefReturnId)
            .WithLocation(0)
            .WithArguments("GetRef");
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ExtensionMethod_WithLog_Reports()
    {
        var test = @"
using ZibStack.NET.Log;

public static class StringExt
{
    [Log]
    public static int {|#0:WordCount|}(this string s) => 1;
}
" + LogAttributeStub;

        // Extension methods are also static so SL0005 (source generator) would fire too,
        // but the analyzer's ZLOG013 surfaces in the IDE without a build.
        var expected = Verify.Diagnostic(LogAttributeValidityAnalyzer.ExtensionMethodId)
            .WithLocation(0)
            .WithArguments("WordCount");
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task PrivateNestedType_WithLog_ReportsContainingType()
    {
        var test = @"
using ZibStack.NET.Log;

public class Outer
{
    private class Inner
    {
        [Log]
        public int {|#0:Get|}(int x) => x;
    }
}
" + LogAttributeStub;

        var expected = Verify.Diagnostic(LogAttributeValidityAnalyzer.PrivateContainingTypeId)
            .WithLocation(0)
            .WithArguments("Get", "Inner");
        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task ClassLevelLog_PrivateMethod_NoDiagnostic()
    {
        // Class-level [Log] only applies to public instance methods. Private members are
        // not picked up — so we must NOT warn (would be a false positive).
        var test = @"
using ZibStack.NET.Log;

[Log]
public class Svc
{
    public int Public(int x) => x;
    private int Internal(int x) => x; // not picked up by class-level [Log], no warning
}
" + LogAttributeStub;
        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task StaticMethod_WithLog_NoDuplicateWarning()
    {
        // Static methods are diagnosed by the source generator (SL0005). The analyzer
        // intentionally stays quiet so users don't see the same problem twice.
        var test = @"
using ZibStack.NET.Log;

public class Svc
{
    [Log]
    public static int Get(int x) => x;
}
" + LogAttributeStub;
        await Verify.VerifyAnalyzerAsync(test);
    }
}
