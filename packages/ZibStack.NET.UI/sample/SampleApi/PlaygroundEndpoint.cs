using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SampleApi;

public static class PlaygroundEndpoint
{
    private static readonly Lazy<(GeneratorDriver UiDriver, GeneratorDriver DtoDriver, GeneratorDriver CoreDriver, GeneratorDriver ValidationDriver)> Drivers = new(LoadGenerators);
    private static readonly Lazy<MetadataReference[]> References = new(LoadReferences);

    public static void MapPlayground(this WebApplication app)
    {
        app.MapPost("/api/playground", (PlaygroundRequest request) =>
        {
            try
            {
                var result = Compile(request.Code ?? "");
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.Ok(new PlaygroundResult { Error = ex.Message });
            }
        });
    }

    private static PlaygroundResult Compile(string code)
    {
        // Wrap user code with usings if not present
        if (!code.Contains("using "))
        {
            code = """
                using System;
                using System.Collections.Generic;
                using ZibStack.NET.UI;
                using ZibStack.NET.Dto;
                using ZibStack.NET.Core;
                using ZibStack.NET.Validation;
                """ + "\n" + code;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(code);

        var compilation = CSharpCompilation.Create("Playground",
            new[] { syntaxTree },
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        // Run generators
        var (uiDriver, dtoDriver, coreDriver, validationDriver) = Drivers.Value;

        var result = new PlaygroundResult();

        // Run UI generator
        var uiResult = uiDriver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
        compilation = updatedCompilation as CSharpCompilation ?? (CSharpCompilation)updatedCompilation;

        // Run Core generator
        var coreResult = coreDriver.RunGeneratorsAndUpdateCompilation(compilation, out updatedCompilation, out _);
        compilation = updatedCompilation as CSharpCompilation ?? (CSharpCompilation)updatedCompilation;

        // Run Dto generator
        var dtoResult = dtoDriver.RunGeneratorsAndUpdateCompilation(compilation, out updatedCompilation, out _);
        compilation = updatedCompilation as CSharpCompilation ?? (CSharpCompilation)updatedCompilation;

        // Run Validation generator
        var validationResult = validationDriver.RunGeneratorsAndUpdateCompilation(compilation, out updatedCompilation, out _);

        // Extract generated sources
        foreach (var genResult in new[] { uiResult, coreResult, dtoResult, validationResult })
        {
            foreach (var result2 in genResult.GetRunResult().Results)
            foreach (var tree in result2.GeneratedSources)
            {
                var fileName = Path.GetFileName(tree.HintName);
                var source = tree.SourceText.ToString();

                if (fileName.Contains("FormJson"))
                {
                    // Extract JSON from the generated source
                    var json = ExtractJsonFromSource(source);
                    if (json != null) result.FormSchema = json;
                }
                else if (fileName.Contains("TableJson"))
                {
                    var json = ExtractJsonFromSource(source);
                    if (json != null) result.TableSchema = json;
                }
            }
        }

        // Collect compilation errors (filter out unresolved types from missing runtime)
        var diagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => !d.Id.StartsWith("CS0012")) // missing assembly references
            .Select(d => d.GetMessage())
            .Take(10)
            .ToList();

        if (diagnostics.Count > 0 && result.FormSchema == null && result.TableSchema == null)
            result.Error = string.Join("\n", diagnostics);

        return result;
    }

    private static string? ExtractJsonFromSource(string source)
    {
        // The generated code contains: @"{ ... json ... }";
        // Extract the JSON between @" and ";
        var match = Regex.Match(source, @"@""(.*?)"";", RegexOptions.Singleline);
        if (match.Success)
        {
            return match.Groups[1].Value.Replace("\"\"", "\"");
        }
        return null;
    }

    private static MetadataReference[] LoadReferences()
    {
        var assemblies = new HashSet<string>();
        var refs = new List<MetadataReference>();

        void AddAssembly(Assembly asm)
        {
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location)) return;
            if (!assemblies.Add(asm.Location)) return;
            refs.Add(MetadataReference.CreateFromFile(asm.Location));
        }

        // Add all currently loaded assemblies (includes System, ASP.NET, EF, etc.)
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            AddAssembly(asm);

        // Ensure key types are included
        AddAssembly(typeof(object).Assembly);
        AddAssembly(typeof(Enumerable).Assembly);
        AddAssembly(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute).Assembly);

        return refs.ToArray();
    }

    private static (GeneratorDriver, GeneratorDriver, GeneratorDriver, GeneratorDriver) LoadGenerators()
    {
        var uiGen = LoadGenerator("ZibStack.NET.UI");
        var dtoGen = LoadGenerator("ZibStack.NET.Dto");
        var coreGen = LoadGenerator("ZibStack.NET.Core");
        var validationGen = LoadGenerator("ZibStack.NET.Validation");

        return (
            CSharpGeneratorDriver.Create(uiGen),
            CSharpGeneratorDriver.Create(dtoGen),
            CSharpGeneratorDriver.Create(coreGen),
            CSharpGeneratorDriver.Create(validationGen)
        );
    }

    private static ISourceGenerator[] LoadGenerator(string assemblyName)
    {
        // Generator DLLs are in the output directory (copied as analyzers)
        var dir = AppContext.BaseDirectory;
        var dllPath = Path.Combine(dir, assemblyName + ".dll");

        if (!File.Exists(dllPath))
        {
            // Try finding in parent dirs
            var current = new DirectoryInfo(dir);
            while (current != null)
            {
                var candidate = Directory.GetFiles(current.FullName, assemblyName + ".dll", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (candidate != null) { dllPath = candidate; break; }
                current = current.Parent;
            }
        }

        if (!File.Exists(dllPath)) return Array.Empty<ISourceGenerator>();

        var assembly = Assembly.LoadFrom(dllPath);
        return assembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i =>
                i.FullName == "Microsoft.CodeAnalysis.IIncrementalGenerator" ||
                i.FullName == "Microsoft.CodeAnalysis.ISourceGenerator"))
            .Select(t =>
            {
                var instance = Activator.CreateInstance(t);
                if (instance is ISourceGenerator sg) return sg;
                if (instance is IIncrementalGenerator ig) return ig.AsSourceGenerator();
                return null;
            })
            .Where(g => g != null)
            .ToArray()!;
    }
}

public record PlaygroundRequest
{
    public string? Code { get; init; }
}

public record PlaygroundResult
{
    public string? FormSchema { get; set; }
    public string? TableSchema { get; set; }
    public string? Error { get; set; }
}
