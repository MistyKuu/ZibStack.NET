using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using ZibStack.NET.Log.Analyzers;

namespace ZibStack.NET.Log.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<UseInterpolatedLogAnalyzer, DefaultVerifier>;

public class UseInterpolatedLogAnalyzerTests
{
    private const string LoggerStubs = @"
namespace Microsoft.Extensions.Logging
{
    public enum LogLevel { Trace, Debug, Information, Warning, Error, Critical }
    public interface ILogger
    {
        bool IsEnabled(LogLevel logLevel);
    }
    public static class LoggerExtensions
    {
        public static void LogTrace(this ILogger logger, string message, params object[] args) { }
        public static void LogDebug(this ILogger logger, string message, params object[] args) { }
        public static void LogInformation(this ILogger logger, string message, params object[] args) { }
        public static void LogWarning(this ILogger logger, string message, params object[] args) { }
        public static void LogError(this ILogger logger, string message, params object[] args) { }
        public static void LogCritical(this ILogger logger, string message, params object[] args) { }
    }
}
";

    [Fact]
    public async Task LogInformation_TemplateWithArgs_Reports()
    {
        var test = @"
using Microsoft.Extensions.Logging;

class MyService
{
    void Do(ILogger logger, string name)
    {
        logger.{|#0:LogInformation|}(""Hello {Name}"", name);
    }
}
" + LoggerStubs;

        var expected = Verify.Diagnostic(UseInterpolatedLogAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("LogInformation");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LogWarning_TemplateWithArgs_Reports()
    {
        var test = @"
using Microsoft.Extensions.Logging;

class MyService
{
    void Do(ILogger logger, int count)
    {
        logger.{|#0:LogWarning|}(""Count is {Count}"", count);
    }
}
" + LoggerStubs;

        var expected = Verify.Diagnostic(UseInterpolatedLogAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("LogWarning");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LogError_TemplateWithMultipleArgs_Reports()
    {
        var test = @"
using Microsoft.Extensions.Logging;

class MyService
{
    void Do(ILogger logger, int id, string err)
    {
        logger.{|#0:LogError|}(""Order {Id} failed: {Error}"", id, err);
    }
}
" + LoggerStubs;

        var expected = Verify.Diagnostic(UseInterpolatedLogAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("LogError");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LogInformation_PlainStringNoArgs_NoDiagnostic()
    {
        // Plain message without args is fine — nothing to interpolate.
        var test = @"
using Microsoft.Extensions.Logging;

class MyService
{
    void Do(ILogger logger)
    {
        logger.LogInformation(""Hello world"");
    }
}
" + LoggerStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task LogInformation_InterpolatedString_NoDiagnostic()
    {
        // Already interpolated — that's the form we suggest.
        var test = @"
using Microsoft.Extensions.Logging;

class MyService
{
    void Do(ILogger logger, string name)
    {
        logger.LogInformation($""Hello {name}"");
    }
}
" + LoggerStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonLoggerMethod_TemplateWithArgs_NoDiagnostic()
    {
        // Method named LogInformation on a non-ILogger type → not flagged.
        var test = @"
class SomeOther
{
    public void LogInformation(string msg, object arg) { }
}

class MyService
{
    void Do()
    {
        var other = new SomeOther();
        other.LogInformation(""Hello {Name}"", ""world"");
    }
}
";

        await Verify.VerifyAnalyzerAsync(test);
    }
}
