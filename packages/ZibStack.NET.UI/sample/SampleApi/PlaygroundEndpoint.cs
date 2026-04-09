using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SampleApi;

public static class PlaygroundEndpoint
{
    private static readonly Lazy<GeneratorDriver> CombinedDriver = new(LoadCombinedDriver);
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

        app.MapGet("/api/playground/attributes", () => Results.Ok(GetAttributes()));
    }

    private static List<AttributeInfo>? _cachedAttributes;

    private static List<AttributeInfo> GetAttributes()
    {
        if (_cachedAttributes != null) return _cachedAttributes;

        // Run generators on empty compilation to collect all PostInit attribute sources
        var emptyTree = CSharpSyntaxTree.ParseText("namespace Empty { }");
        var compilation = CSharpCompilation.Create("AttrScan",
            new[] { emptyTree }, References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var (uiDriver, dtoDriver, coreDriver, validationDriver) = Drivers.Value;
        var attrs = new List<AttributeInfo>();

        var drivers = new List<(GeneratorDriver driver, string package)> {
            (uiDriver, "UI"), (dtoDriver, "Dto"), (coreDriver, "Core"), (validationDriver, "Validation")
        };

        // Note: Log and Aop use interceptors (not source generators for attributes)
        // Their attributes are added manually below

        foreach (var (driver, package) in drivers)
        {
            var result = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
            foreach (var genResult in result.GetRunResult().Results)
            foreach (var src in genResult.GeneratedSources)
            {
                if (!src.HintName.EndsWith("Attribute.g.cs")) continue;
                var code = src.SourceText.ToString();
                var info = ParseAttributeSource(code, package);
                if (info != null) attrs.Add(info);
            }
        }

        // Log and Aop use interceptors (not source-generated attributes) — add manually
        attrs.Add(new AttributeInfo("Log", "method/class", "Log", "Automatic entry/exit/exception logging with zero allocation", "Level?, ObjectLogging?"));
        attrs.Add(new AttributeInfo("Sensitive", "parameter", "Log", "Masks parameter value in log output", ""));
        attrs.Add(new AttributeInfo("ZibLogDefaults", "assembly", "Log", "Override assembly-level logging defaults", "EntryExitLevel?, ObjectLogging?"));
        attrs.Add(new AttributeInfo("Trace", "method", "Aop", "OpenTelemetry-compatible tracing — creates Activity spans", ""));
        attrs.Add(new AttributeInfo("Timing", "method", "Aop", "Lightweight method timing via ITimingRecorder", ""));
        attrs.Add(new AttributeInfo("AspectHandler", "class", "Aop", "Links an aspect attribute to its handler type", "handlerType"));
        attrs.Add(new AttributeInfo("AspectAttribute", "class", "Aop", "Base class for custom aspect attributes", ""));

        _cachedAttributes = attrs.OrderBy(a => a.Package).ThenBy(a => a.Name).ToList();
        return _cachedAttributes;
    }

    private static AttributeInfo? ParseAttributeSource(string source, string package)
    {
        // Extract class name
        var classMatch = Regex.Match(source, @"sealed class (\w+Attribute)");
        if (!classMatch.Success) return null;
        var fullName = classMatch.Groups[1].Value;
        var name = fullName.Replace("Attribute", "");

        // Extract summary
        var summaryMatch = Regex.Match(source, @"<summary>(.*?)</summary>", RegexOptions.Singleline);
        var description = summaryMatch.Success
            ? Regex.Replace(summaryMatch.Groups[1].Value.Trim(), @"\s+", " ").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&")
            : "";

        // Extract target from AttributeUsage
        var targetMatch = Regex.Match(source, @"AttributeTargets\.(\w+)");
        var target = targetMatch.Success ? targetMatch.Groups[1].Value.ToLowerInvariant() : "class";
        if (target.Contains("|")) target = "class/property";

        // Extract properties (public ... { get; set; } or public ... { get; })
        var props = new List<string>();
        foreach (Match m in Regex.Matches(source, @"public [\w<>?]+ (\w+) \{ get;"))
        {
            var propName = m.Groups[1].Value;
            if (propName == "TargetType") propName = "targetType";
            props.Add(propName);
        }

        // Extract constructor params
        foreach (Match m in Regex.Matches(source, @"(\w+Attribute)\((.*?)\)", RegexOptions.Singleline))
        {
            var paramStr = m.Groups[2].Value;
            foreach (Match p in Regex.Matches(paramStr, @"(?:string|int|System\.Type|bool)\s+(\w+)"))
            {
                var pName = p.Groups[1].Value;
                if (!props.Contains(pName)) props.Insert(0, pName);
            }
        }

        return new AttributeInfo(name, target, package, description, string.Join(", ", props));
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

        var result = new PlaygroundResult();

        // Run ALL generators together so PostInit attributes are visible across generators
        var driver = CombinedDriver.Value;
        var driverResult = driver.RunGeneratorsAndUpdateCompilation(compilation, out var finalCompilation, out _);

        // Categorize all generated sources
        foreach (var genResult in new[] { driverResult })
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

        var diagnostics = finalCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => !d.Id.StartsWith("CS0012") && !d.Id.StartsWith("CS0009"))
            .Select(d => d.GetMessage())
            .Take(10)
            .ToList();

        // Only report errors if no schemas were generated at all
        // (generated code may have errors from missing ASP.NET/EF refs — that's OK for schema preview)
        if (diagnostics.Count > 0 && result.FormSchema == null && result.TableSchema == null && result.Generated.Count == 0)
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
        AddAssembly(typeof(Queryable).Assembly);
        AddAssembly(typeof(IQueryable).Assembly);
        AddAssembly(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute).Assembly);

        // Ensure all core runtime assemblies are included
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
        {
            if (assemblies.Contains(dll)) continue;
            try
            {
                // Skip native DLLs by trying to open as managed
                using var fs = File.OpenRead(dll);
                using var peReader = new System.Reflection.PortableExecutable.PEReader(fs);
                if (!peReader.HasMetadata) continue;
                refs.Add(MetadataReference.CreateFromFile(dll));
                assemblies.Add(dll);
            }
            catch { }
        }
        return refs.ToArray();
    }

    private static GeneratorDriver LoadCombinedDriver()
    {
        var allGens = new List<ISourceGenerator>();
        foreach (var name in new[] { "ZibStack.NET.Validation", "ZibStack.NET.Core", "ZibStack.NET.UI", "ZibStack.NET.Dto" })
            allGens.AddRange(LoadGenerator(name));
        return CSharpGeneratorDriver.Create(allGens.ToArray());
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
        try
        {
            var assembly = Assembly.LoadFrom(dllPath);
            return assembly.GetTypes()
                .Where(t => t.GetInterfaces().Any(i => i.FullName == "Microsoft.CodeAnalysis.IIncrementalGenerator" || i.FullName == "Microsoft.CodeAnalysis.ISourceGenerator"))
                .Select(t => { var i = Activator.CreateInstance(t); if (i is ISourceGenerator sg) return sg; if (i is IIncrementalGenerator ig) return ig.AsSourceGenerator(); return null; })
                .Where(g => g != null).ToArray()!;
        }
        catch { return Array.Empty<ISourceGenerator>(); }
    }
}

public record PlaygroundRequest { public string? Code { get; init; } }
public record GeneratedFile(string Category, string FileName, string Source);
public record AttributeInfo(string Name, string Target, string Package, string Description, string Props);

public record PlaygroundResult
{
    public string? FormSchema { get; set; }
    public string? TableSchema { get; set; }
    public string? Error { get; set; }
    public List<GeneratedFile> Generated { get; set; } = new();
}
