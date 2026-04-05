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

        var sourceNames = generatedSources.Select(s => s.HintName).ToList();
        Assert.DoesNotContain("LogAttribute.g.cs", sourceNames);
    }

    [Fact]
    public void Generator_ReportsError_ForStaticMethods()
    {
        var source = @"
using ZibStack.NET.Log;

public class MyService
{
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

public class MyService
{
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

        var generatedNames = generatedSources.Select(s => s.HintName).ToList();
        Assert.DoesNotContain("LogAttribute.g.cs", generatedNames);
    }

    private static (Compilation compilation, ImmutableArray<Diagnostic> diagnostics, ImmutableArray<(string HintName, string Source)> generatedSources)
        RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ZibStack.NET.Log.LogAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ZibStack.NET.Aop.AspectAttribute).Assembly.Location),
        };

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var systemRuntime = Path.Combine(runtimeDir, "System.Runtime.dll");
        if (File.Exists(systemRuntime))
            references.Add(MetadataReference.CreateFromFile(systemRuntime));

        var netstandard = Path.Combine(runtimeDir, "netstandard.dll");
        if (File.Exists(netstandard))
            references.Add(MetadataReference.CreateFromFile(netstandard));

        var loggingAbstractions = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Extensions.Logging.Abstractions");
        if (loggingAbstractions is not null)
        {
            references.Add(MetadataReference.CreateFromFile(loggingAbstractions.Location));
        }
        else
        {
            var loggerStub = CSharpSyntaxTree.ParseText(@"
namespace Microsoft.Extensions.Logging
{
    public enum LogLevel { Trace, Debug, Information, Warning, Error, Critical, None }
    public interface ILogger { }
    public interface ILogger<out T> : ILogger { }
}
");
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
