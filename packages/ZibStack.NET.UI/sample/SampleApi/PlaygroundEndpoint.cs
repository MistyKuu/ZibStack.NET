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

        var (uiDriver, dtoDriver, coreDriver, validationDriver) = Drivers.Value;
        var result = new PlaygroundResult();

        var uiResult = uiDriver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
        compilation = updatedCompilation as CSharpCompilation ?? (CSharpCompilation)updatedCompilation;

        var coreResult = coreDriver.RunGeneratorsAndUpdateCompilation(compilation, out updatedCompilation, out _);
        compilation = updatedCompilation as CSharpCompilation ?? (CSharpCompilation)updatedCompilation;

        var dtoResult = dtoDriver.RunGeneratorsAndUpdateCompilation(compilation, out updatedCompilation, out _);
        compilation = updatedCompilation as CSharpCompilation ?? (CSharpCompilation)updatedCompilation;

        var validationResult = validationDriver.RunGeneratorsAndUpdateCompilation(compilation, out updatedCompilation, out _);

        // Categorize all generated sources
        foreach (var genResult in new[] { uiResult, coreResult, dtoResult, validationResult })
        {
            foreach (var result2 in genResult.GetRunResult().Results)
            foreach (var gen in result2.GeneratedSources)
            {
                var name = gen.HintName;
                var source = gen.SourceText.ToString();

                // Skip attribute definitions and infrastructure
                if (name.EndsWith("Attribute.g.cs") || name == "PatchField.g.cs"
                    || name == "PaginatedResponse.g.cs" || name == "SortDirection.g.cs"
                    || name == "IDtoValidator.g.cs" || name == "ICrudStore.g.cs"
                    || name == "CrudOperations.g.cs" || name == "ApiStyle.g.cs"
                    || name == "FormDescriptor.g.cs" || name == "TableDescriptor.g.cs")
                    continue;

                // Extract JSON schemas
                if (name.Contains("FormJson"))
                {
                    var json = ExtractJsonFromSource(source);
                    if (json != null) result.FormSchema = json;
                }
                else if (name.Contains("TableJson"))
                {
                    var json = ExtractJsonFromSource(source);
                    if (json != null) result.TableSchema = json;
                }

                // Categorize by file name pattern
                if (name.Contains(".Endpoints."))
                    result.Generated.Add(new GeneratedFile("Endpoints", name, source));
                else if (name.Contains(".Create."))
                    result.Generated.Add(new GeneratedFile("DTOs", name, source));
                else if (name.Contains(".Update."))
                    result.Generated.Add(new GeneratedFile("DTOs", name, source));
                else if (name.Contains(".Response."))
                    result.Generated.Add(new GeneratedFile("DTOs", name, source));
                else if (name.Contains(".ListItem."))
                    result.Generated.Add(new GeneratedFile("DTOs", name, source));
                else if (name.Contains(".Query."))
                    result.Generated.Add(new GeneratedFile("Query", name, source));
                else if (name.Contains(".Entity.") || name.Contains("EntityConfigurations"))
                    result.Generated.Add(new GeneratedFile("Database", name, source));
                else if (name.Contains("CrudStores"))
                    result.Generated.Add(new GeneratedFile("Database", name, source));
                else if (name.Contains(".Form.") && !name.Contains("Json"))
                    result.Generated.Add(new GeneratedFile("UI", name, source));
                else if (name.Contains(".Table.") && !name.Contains("Json"))
                    result.Generated.Add(new GeneratedFile("UI", name, source));
                else if (name.Contains("FormJson") || name.Contains("TableJson"))
                    result.Generated.Add(new GeneratedFile("UI", name, source));
                else if (!name.EndsWith("Attribute.g.cs"))
                    result.Generated.Add(new GeneratedFile("Other", name, source));
            }
        }

        var diagnostics = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => !d.Id.StartsWith("CS0012"))
            .Select(d => d.GetMessage())
            .Take(10)
            .ToList();

        if (diagnostics.Count > 0 && result.FormSchema == null && result.TableSchema == null)
            result.Error = string.Join("\n", diagnostics);

        return result;
    }

    private static string? ExtractJsonFromSource(string source)
    {
        var match = Regex.Match(source, @"@""(.*?)"";", RegexOptions.Singleline);
        if (match.Success)
            return match.Groups[1].Value.Replace("\"\"", "\"");
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
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) AddAssembly(asm);
        AddAssembly(typeof(object).Assembly);
        AddAssembly(typeof(Enumerable).Assembly);
        AddAssembly(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute).Assembly);
        return refs.ToArray();
    }

    private static (GeneratorDriver, GeneratorDriver, GeneratorDriver, GeneratorDriver) LoadGenerators()
    {
        return (
            CSharpGeneratorDriver.Create(LoadGenerator("ZibStack.NET.UI")),
            CSharpGeneratorDriver.Create(LoadGenerator("ZibStack.NET.Dto")),
            CSharpGeneratorDriver.Create(LoadGenerator("ZibStack.NET.Core")),
            CSharpGeneratorDriver.Create(LoadGenerator("ZibStack.NET.Validation"))
        );
    }

    private static ISourceGenerator[] LoadGenerator(string assemblyName)
    {
        var dir = AppContext.BaseDirectory;
        var dllPath = Path.Combine(dir, assemblyName + ".dll");
        if (!File.Exists(dllPath))
        {
            var current = new DirectoryInfo(dir);
            while (current != null)
            {
                var candidate = Directory.GetFiles(current.FullName, assemblyName + ".dll", SearchOption.AllDirectories).FirstOrDefault();
                if (candidate != null) { dllPath = candidate; break; }
                current = current.Parent;
            }
        }
        if (!File.Exists(dllPath)) return Array.Empty<ISourceGenerator>();
        var assembly = Assembly.LoadFrom(dllPath);
        return assembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i => i.FullName == "Microsoft.CodeAnalysis.IIncrementalGenerator" || i.FullName == "Microsoft.CodeAnalysis.ISourceGenerator"))
            .Select(t => { var i = Activator.CreateInstance(t); if (i is ISourceGenerator sg) return sg; if (i is IIncrementalGenerator ig) return ig.AsSourceGenerator(); return null; })
            .Where(g => g != null).ToArray()!;
    }
}

public record PlaygroundRequest { public string? Code { get; init; } }

public record GeneratedFile(string Category, string FileName, string Source);

public record PlaygroundResult
{
    public string? FormSchema { get; set; }
    public string? TableSchema { get; set; }
    public string? Error { get; set; }
    public List<GeneratedFile> Generated { get; set; } = new();
}
