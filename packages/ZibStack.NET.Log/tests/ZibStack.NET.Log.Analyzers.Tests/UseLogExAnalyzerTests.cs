using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using ZibStack.NET.Log.Analyzers;

namespace ZibStack.NET.Log.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<UseLogExAnalyzer, DefaultVerifier>;

public class UseLogExAnalyzerTests
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
    public async Task LogInformation_WithInterpolatedString_Reports()
    {
        var test = @"
using Microsoft.Extensions.Logging;

class MyService
{
    void Do(ILogger logger, string name)
    {
        logger.{|#0:LogInformation|}($""Hello {name}"");
    }
}
" + LoggerStubs;

        var expected = Verify.Diagnostic(UseLogExAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("LogInformationEx", "LogInformation");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LogWarning_WithInterpolatedString_Reports()
    {
        var test = @"
using Microsoft.Extensions.Logging;

class MyService
{
    void Do(ILogger logger, int count)
    {
        logger.{|#0:LogWarning|}($""Count is {count}"");
    }
}
" + LoggerStubs;

        var expected = Verify.Diagnostic(UseLogExAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("LogWarningEx", "LogWarning");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LogError_WithInterpolatedString_Reports()
    {
        var test = @"
using Microsoft.Extensions.Logging;

class MyService
{
    void Do(ILogger logger, string err)
    {
        logger.{|#0:LogError|}($""Error: {err}"");
    }
}
" + LoggerStubs;

        var expected = Verify.Diagnostic(UseLogExAnalyzer.DiagnosticId)
            .WithLocation(0)
            .WithArguments("LogErrorEx", "LogError");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task LogInformation_WithPlainString_NoDiagnostic()
    {
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
    public async Task LogInformation_WithTemplateString_NoDiagnostic()
    {
        var test = @"
using Microsoft.Extensions.Logging;

class MyService
{
    void Do(ILogger logger, string name)
    {
        logger.LogInformation(""Hello {Name}"", name);
    }
}
" + LoggerStubs;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task NonLoggerMethod_WithInterpolatedString_NoDiagnostic()
    {
        var test = @"
class SomeOther
{
    public void LogInformation(string msg) { }
}

class MyService
{
    void Do()
    {
        var other = new SomeOther();
        other.LogInformation($""Hello {42}"");
    }
}
";

        await Verify.VerifyAnalyzerAsync(test);
    }
}
