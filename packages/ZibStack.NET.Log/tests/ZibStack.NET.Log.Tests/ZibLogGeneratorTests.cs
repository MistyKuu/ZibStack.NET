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

    // === Integration tests: run generator and assert the combined compilation has no errors ===
    // These cover every [Log] placement the user asked about (class, generic class, method,
    // generic method, interface, generic interface, base class, generic base) plus a few
    // combinations. A passing test means the generated code actually compiles — which the
    // previous ParseMethod-only tests did NOT verify.

    [Fact]
    public void Integration_LogOnClass_PlainMethod_Compiles()
    {
        AssertCompilesCleanly(@"
using ZibStack.NET.Log;

[Log]
public class OrderService
{
    public int GetOrder(int id) => id;
}

public class Caller
{
    public int Run() { var s = new OrderService(); return s.GetOrder(42); }
}
");
    }

    [Fact]
    public void Integration_LogOnMethod_NonGeneric_Compiles()
    {
        AssertCompilesCleanly(@"
using ZibStack.NET.Log;

public class OrderService
{
    [Log]
    public int GetOrder(int id) => id;
}

public class Caller
{
    public int Run() { var s = new OrderService(); return s.GetOrder(42); }
}
");
    }

    [Fact]
    public void Integration_LogOnGenericMethod_Compiles()
    {
        AssertCompilesCleanly(@"
using ZibStack.NET.Log;

public class Repo
{
    [Log]
    public T Fetch<T>(int id) where T : class, new() => new T();
}

public class Caller
{
    public string? Run() { var r = new Repo(); return r.Fetch<string>(1); }
}
");
    }

    [Fact]
    public void Integration_LogOnClass_WithGenericMethodMixed_Compiles()
    {
        // This is the user's exact complaint: [Log] on a class that has a mix of
        // plain and generic methods. Previously the generic method emitted
        // LoggerMessage.Define<T> which references an out-of-scope T.
        AssertCompilesCleanly(@"
using ZibStack.NET.Log;

[Log]
public class MixedService
{
    public int Plain(int id) => id;
    public T Generic<T>(int id) where T : class, new() => new T();
}

public class Caller
{
    public int A() => new MixedService().Plain(1);
    public string? B() => new MixedService().Generic<string>(1);
}
");
    }

    [Fact]
    public void Integration_LogOnGenericClass_Compiles()
    {
        AssertCompilesCleanly(@"
using ZibStack.NET.Log;

[Log]
public class Repo<T> where T : class, new()
{
    public T Get(int id) => new T();
}

public class Caller
{
    public string? Run() => new Repo<string>().Get(1);
}
");
    }

    [Fact]
    public void Integration_LogOnGenericClass_WithGenericMethod_Compiles()
    {
        AssertCompilesCleanly(@"
using ZibStack.NET.Log;

[Log]
public class Repo<T> where T : class
{
    public TOut Convert<TOut>(T input) where TOut : class, new() => new TOut();
}

public class Caller
{
    public object? Run() => new Repo<string>().Convert<object>(""x"");
}
");
    }

    [Fact]
    public void Integration_LogOnInterfaceMethod_CallViaInterface_Compiles()
    {
        AssertCompilesCleanly(@"
using ZibStack.NET.Log;

public interface IOrderService
{
    [Log]
    int GetOrder(int id);
}

public class OrderService : IOrderService
{
    public int GetOrder(int id) => id;
}

public class Caller
{
    public int Run(IOrderService svc) => svc.GetOrder(42);
}
");
    }

    [Fact]
    public void Integration_LogOnGenericInterface_CallViaInterface_Compiles()
    {
        AssertCompilesCleanly(@"
using ZibStack.NET.Log;

public interface IRepo<T> where T : class
{
    [Log]
    T Get(int id);
}

public class StringRepo : IRepo<string>
{
    public string Get(int id) => id.ToString();
}

public class Caller
{
    public string Run(IRepo<string> r) => r.Get(1);
}
");
    }

    [Fact]
    public void Integration_ClassLevelLog_CallViaInterface_Compiles()
    {
        // User's main complaint: [Log] on class + DI-style call via interface ref
        // should also be intercepted (previously silently no-op).
        AssertCompilesCleanly(@"
using ZibStack.NET.Log;

public interface IOrderService { int GetOrder(int id); }

[Log]
public class OrderService : IOrderService
{
    public int GetOrder(int id) => id;
}

public class Caller
{
    public int RunViaIface(IOrderService svc) => svc.GetOrder(42);
    public int RunViaConcrete() => new OrderService().GetOrder(42);
}
");
    }

    [Fact]
    public void Integration_LogOnGenericBaseClass_DerivedUsesIt_Compiles()
    {
        AssertCompilesCleanly(@"
using ZibStack.NET.Log;

[Log]
public abstract class BaseService<T> where T : class
{
    public virtual T Process(T input) => input;
}

public class UserService : BaseService<string> { }

public class Caller
{
    public string Run() => new UserService().Process(""hi"");
}
");
    }

    [Fact]
    public void Integration_LogOnBaseClass_DerivedUsesIt_Compiles()
    {
        AssertCompilesCleanly(@"
using ZibStack.NET.Log;

[Log]
public abstract class BaseService
{
    public virtual int Process(int x) => x;
}

public class UserService : BaseService { }

public class Caller
{
    public int Run() => new UserService().Process(5);
}
");
    }

    [Fact]
    public void Integration_LogOnAsyncMethod_Compiles()
    {
        AssertCompilesCleanly(@"
using ZibStack.NET.Log;
using System.Threading.Tasks;

public class Svc
{
    [Log]
    public async Task<int> WorkAsync(int x) { await Task.CompletedTask; return x; }
}

public class Caller
{
    public async Task<int> Run() => await new Svc().WorkAsync(1);
}
");
    }

    [Fact]
    public void Integration_LogOnGenericAsyncMethod_Compiles()
    {
        AssertCompilesCleanly(@"
using ZibStack.NET.Log;
using System.Threading.Tasks;

public class Svc
{
    [Log]
    public async Task<T> WorkAsync<T>(T input) { await Task.CompletedTask; return input; }
}

public class Caller
{
    public async Task<string> Run() => await new Svc().WorkAsync<string>(""x"");
}
");
    }

    [Fact]
    public void Integration_ClassWithMultipleLogMethods_CachedLoggerEmittedOnce()
    {
        // Regression guard for the state-across-runs bug: first run emitted __cachedLogger
        // correctly, but the same emitter instance on a second run silently skipped it,
        // producing a generated file that referenced an undefined __cachedLogger field.
        var src = @"
using ZibStack.NET.Log;

public class Svc
{
    [Log] public int A(int x) => x;
    [Log] public int B(int x) => x;
    [Log] public int C(int x) => x;
}

public class Caller
{
    public int Run() { var s = new Svc(); return s.A(1) + s.B(2) + s.C(3); }
}
";
        // First run.
        AssertCompilesCleanly(src);
        // Second run — same source, fresh generator. If the emitter kept state globally
        // (it used to), __cachedLogger would be dropped here.
        AssertCompilesCleanly(src);
    }

    [Fact]
    public void Integration_IncrementalRerun_ReemitsCachedLogger()
    {
        // Simulate the IDE re-invoking the same driver after a source edit. The old
        // implementation kept a HashSet<string> _emittedClasses on the emitter instance;
        // after the first run it contained "Svc", so the second RunGeneratorsAndUpdate
        // would skip the __cachedLogger field emission on the same driver.
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures(new[]
            {
                new KeyValuePair<string, string>("InterceptorsNamespaces", "ZibStack.Generated"),
                new KeyValuePair<string, string>("InterceptorsPreviewNamespaces", "ZibStack.Generated"),
            });

        var src1 = CSharpSyntaxTree.ParseText(@"
using ZibStack.NET.Log;
public class Svc { [Log] public int A(int x) => x; }
public class Caller1 { public int R() => new Svc().A(1); }
", parseOptions);
        var src2 = CSharpSyntaxTree.ParseText(@"
using ZibStack.NET.Log;
public class Svc { [Log] public int A(int x) => x; [Log] public int B(int x) => x; }
public class Caller2 { public int R() { var s = new Svc(); return s.A(1) + s.B(2); } }
", parseOptions);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ZibStack.NET.Log.LogAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ZibStack.NET.Aop.AspectAttribute).Assembly.Location),
        };
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dll in new[] { "System.Runtime.dll", "netstandard.dll" })
        {
            var path = Path.Combine(runtimeDir, dll);
            if (File.Exists(path)) references.Add(MetadataReference.CreateFromFile(path));
        }
        var loggingAbstractions = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Microsoft.Extensions.Logging.Abstractions");
        if (loggingAbstractions is not null)
            references.Add(MetadataReference.CreateFromFile(loggingAbstractions.Location));

        var c1 = CSharpCompilation.Create("T", new[] { src1 }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        // Typed as base GeneratorDriver because RunGeneratorsAndUpdateCompilation
        // returns the base type — assigning its result back into a CSharpGeneratorDriver
        // local fails with CS0266.
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new ZibLogGenerator().AsSourceGenerator() }, parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(c1, out var outC1, out _);
        var errs1 = outC1.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errs1.Count == 0, "First run errors:\n" + string.Join("\n", errs1));

        var c2 = c1.ReplaceSyntaxTree(src1, src2);
        driver = driver.RunGeneratorsAndUpdateCompilation(c2, out var outC2, out _);
        var errs2 = outC2.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errs2.Count == 0, "Second run errors:\n" + string.Join("\n", errs2));
    }

    private static (Compilation compilation, ImmutableArray<Diagnostic> diagnostics, ImmutableArray<(string HintName, string Source)> generatedSources)
        RunGenerator(string source)
    {
        // Interceptors are opt-in per namespace. The generator emits into
        // ZibStack.Generated, so enable that namespace on the test compilation the same
        // way the package's buildTransitive props do in real consumer projects.
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures(new[]
            {
                new KeyValuePair<string, string>("InterceptorsNamespaces", "ZibStack.Generated"),
                new KeyValuePair<string, string>("InterceptorsPreviewNamespaces", "ZibStack.Generated"),
            });
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ZibStack.NET.Log.LogAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ZibStack.NET.Aop.AspectAttribute).Assembly.Location),
        };

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dll in new[] { "System.Runtime.dll", "netstandard.dll", "System.Threading.Tasks.dll", "System.Collections.dll", "System.Linq.dll" })
        {
            var path = Path.Combine(runtimeDir, dll);
            if (File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

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
    public interface ILogger { bool IsEnabled(LogLevel level); void Log(LogLevel level, string message, params object?[] args); void Log(LogLevel level, System.Exception ex, string message, params object?[] args); }
    public interface ILogger<out T> : ILogger { }
    public readonly struct EventId { public EventId(int id, string name) {} }
    public static class LoggerMessage
    {
        public static System.Action<ILogger, System.Exception?> Define(LogLevel level, EventId id, string message) => (_,_)=>{};
        public static System.Action<ILogger, T1, System.Exception?> Define<T1>(LogLevel level, EventId id, string message) => (_,_,_)=>{};
        public static System.Action<ILogger, T1, T2, System.Exception?> Define<T1, T2>(LogLevel level, EventId id, string message) => (_,_,_,_)=>{};
        public static System.Action<ILogger, T1, T2, T3, System.Exception?> Define<T1, T2, T3>(LogLevel level, EventId id, string message) => (_,_,_,_,_)=>{};
    }
}
", parseOptions);
            syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
            return RunGeneratorWithTrees(new[] { syntaxTree, loggerStub }, references);
        }

        return RunGeneratorWithTrees(new[] { syntaxTree }, references);
    }

    private static (Compilation compilation, ImmutableArray<Diagnostic> diagnostics, ImmutableArray<(string HintName, string Source)> generatedSources)
        RunGeneratorWithTrees(SyntaxTree[] trees, List<MetadataReference> references)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "ZibLogTests",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var generator = new ZibLogGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            parseOptions: (CSharpParseOptions)trees[0].Options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        var runResult = driver.GetRunResult();
        var generatedSources = runResult.GeneratedTrees
            .Select(t => (
                HintName: Path.GetFileName(t.FilePath),
                Source: t.GetText().ToString()))
            .ToImmutableArray();

        return (outputCompilation, generatorDiagnostics, generatedSources);
    }

    /// <summary>
    /// Runs the generator and asserts the resulting compilation (user source + generated
    /// files) has zero error-severity diagnostics. On failure, dumps every generated file
    /// alongside the errors so regressions are diagnosable without a debugger.
    /// </summary>
    private static void AssertCompilesCleanly(string source)
    {
        var (compilation, genDiags, generated) = RunGenerator(source);
        var generatorErrors = genDiags.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        var compileErrors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            // CS1701/CS1702: assembly reference mismatch warnings-as-errors in some configs; ignore.
            .Where(d => d.Id is not "CS1701" and not "CS1702")
            .ToList();

        if (generatorErrors.Count == 0 && compileErrors.Count == 0)
            return;

        var dump = new System.Text.StringBuilder();
        dump.AppendLine("=== Generator diagnostics ===");
        foreach (var d in generatorErrors) dump.AppendLine(d.ToString());
        dump.AppendLine("=== Compile diagnostics ===");
        foreach (var d in compileErrors) dump.AppendLine(d.ToString());
        dump.AppendLine("=== Generated files ===");
        foreach (var (name, src) in generated)
        {
            dump.AppendLine($"--- {name} ---");
            dump.AppendLine(src);
        }
        Assert.Fail(dump.ToString());
    }
}
