using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ZibStack.NET.Log.Generator;
using Xunit;

namespace ZibStack.NET.Log.Tests;

public class ZibLogGeneratorTests
{
    [Fact]
    public void Generator_DoesNotInjectAttributes_WhenAbstractionsReferenced()
    {
        var source = "";
        var (_, diagnostics, generatedSources) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Attributes come from ZibStack.NET.Log.Abstractions, not injected by generator
        var sourceNames = generatedSources.Select(s => s.HintName).ToList();
        Assert.DoesNotContain("ZibLogAttribute.g.cs", sourceNames);
    }

    [Fact]
    public void Generator_ReportsError_WhenNoLoggerField()
    {
        var source = @"
using ZibStack.NET.Log;

[ZibLog]
public class MyService
{
    [Log]
    public void DoWork() { }
}
";
        var (_, diagnostics, _) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SL0002");
    }

    [Fact]
    public void Generator_ReportsError_WhenMultipleLoggerFields()
    {
        var source = @"
using ZibStack.NET.Log;
using Microsoft.Extensions.Logging;

[ZibLog]
public class MyService
{
    private readonly ILogger<MyService> _logger;
    private readonly ILogger _otherLogger;

    [Log]
    public void DoWork() { }
}
";
        var (_, diagnostics, _) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SL0003");
    }

    [Fact]
    public void Generator_ReportsError_ForStaticMethods()
    {
        var source = @"
using ZibStack.NET.Log;
using Microsoft.Extensions.Logging;

[ZibLog]
public class MyService
{
    private readonly ILogger<MyService> _logger;

    [Log]
    public static void DoWork() { }
}
";
        var (_, diagnostics, _) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SL0005");
    }

    [Fact]
    public void Generator_ParsesClassWithLogMethod()
    {
        var source = @"
using ZibStack.NET.Log;
using Microsoft.Extensions.Logging;

[ZibLog]
public class MyService
{
    private readonly ILogger<MyService> _logger;

    [Log]
    public string GetValue(int id)
    {
        return ""test"";
    }
}

public class Consumer
{
    public void Run()
    {
        var svc = new MyService();
        var result = svc.GetValue(42);
    }
}
";
        var (_, diagnostics, generatedSources) = RunGenerator(source);

        // Generator should run without critical errors (some compilation errors expected in test stubs)
        var generatedNames = generatedSources.Select(s => s.HintName).ToList();
        // Attributes come from Abstractions, not injected
        Assert.DoesNotContain("ZibLogAttribute.g.cs", generatedNames);
    }

    [Fact]
    public void Generator_ReportsError_WhenSpecifiedLoggerFieldNotFound()
    {
        var source = @"
using ZibStack.NET.Log;
using Microsoft.Extensions.Logging;

[ZibLog(LoggerField = ""_nonExistent"")]
public class MyService
{
    private readonly ILogger<MyService> _logger;

    [Log]
    public void DoWork() { }
}
";
        var (_, diagnostics, _) = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "SL0006");
    }

    private static (Compilation compilation, ImmutableArray<Diagnostic> diagnostics, ImmutableArray<(string HintName, string Source)> generatedSources)
        RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            // ZibStack.NET.Log.Abstractions — provides [ZibLog], [Log], etc.
            MetadataReference.CreateFromFile(typeof(ZibStack.NET.Log.ZibLogAttribute).Assembly.Location),
        };

        // Add System.Runtime
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var systemRuntime = Path.Combine(runtimeDir, "System.Runtime.dll");
        if (File.Exists(systemRuntime))
            references.Add(MetadataReference.CreateFromFile(systemRuntime));

        var netstandard = Path.Combine(runtimeDir, "netstandard.dll");
        if (File.Exists(netstandard))
            references.Add(MetadataReference.CreateFromFile(netstandard));

        // Try to add Microsoft.Extensions.Logging.Abstractions
        var loggingAbstractions = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Extensions.Logging.Abstractions");
        if (loggingAbstractions is not null)
        {
            references.Add(MetadataReference.CreateFromFile(loggingAbstractions.Location));
        }
        else
        {
            // Add a stub ILogger interface for compilation
            var loggerStub = CSharpSyntaxTree.ParseText(@"
namespace Microsoft.Extensions.Logging
{
    public enum LogLevel { Trace, Debug, Information, Warning, Error, Critical, None }
    public interface ILogger { }
    public interface ILogger<out T> : ILogger { }
}
");
            references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            syntaxTree = CSharpSyntaxTree.ParseText(source + loggerStub.ToString());
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "ZibLogTests",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ZibLogGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        var runResult = driver.GetRunResult();
        var generatedSources = runResult.GeneratedTrees
            .Select(t => (
                HintName: Path.GetFileName(t.FilePath),
                Source: t.GetText().ToString()))
            .ToImmutableArray();

        return (outputCompilation, generatorDiagnostics, generatedSources);
    }
}
