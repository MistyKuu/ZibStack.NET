using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// Regression test: MigrationDownDto was emitted ~15 times in SingleFile mode
/// when referenced via a diamond-shaped type graph (MigrationDto.Down is nullable
/// MigrationDownDto, and MigrationDto appears in both PhasesDto.Expand and
/// PhasesDto.Contract). The transitive discovery walker must deduplicate by type
/// identity so each schema is emitted exactly once.
/// </summary>
public class DiamondDeduplicationTests
{
    private const string Source = """
        using System.Collections.Generic;
        using ZibStack.NET.TypeGen;

        namespace DiamondTest;

        [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
        public record RegisterSolutionRequestDto(
            string Name,
            string? Description,
            List<SolutionVersionDto> Versions,
            List<SolutionDependencyDto>? Dependencies);

        public record SolutionDependencyDto(string SolutionName, string MinVersion);

        public record SolutionVersionDto(string Version, PhasesDto Phases);

        public record PhasesDto()
        {
            public required List<MigrationDto> Expand { get; init; } = [];
            public required List<MigrationDto> Contract { get; init; } = [];
        }

        public record MigrationUpDto(string Action);

        public record MigrationDownDto(string Action);

        public record MigrationDto(
            string Name,
            MigrationUpDto Up,
            MigrationDownDto? Down,
            string? Description);
        """;

    [Fact]
    public void TransitiveDiscovery_DiamondGraph_NoDuplicateClasses()
    {
        var model = BuildFullModel();

        // Each type should appear exactly once in the model
        var classCounts = model.Classes
            .GroupBy(c => c.CSharpFullName)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {g.Count()}x")
            .ToList();

        Assert.Empty(classCounts);
    }

    [Fact]
    public void SingleFile_DiamondGraph_NoDeclarationDuplicates()
    {
        var model = BuildFullModel();
        var settings = new GlobalSettings
        {
            TypeScript = { FileLayout = TypeScriptFileLayout.SingleFile, SingleFileName = "index.ts", OutputDir = "." },
        };
        var files = TypeScriptEmitter.Emit(model, settings);
        var content = Assert.Single(files).Content;

        // MigrationDownDto declaration should appear exactly once
        var downCount = Regex.Matches(content, @"export\s+interface\s+MigrationDownDto").Count;
        Assert.True(downCount == 1, $"MigrationDownDto emitted {downCount} times, expected 1.\n\nFull output:\n{content}");

        // MigrationUpDto same
        var upCount = Regex.Matches(content, @"export\s+interface\s+MigrationUpDto").Count;
        Assert.True(upCount == 1, $"MigrationUpDto emitted {upCount} times, expected 1.");

        // MigrationDto same
        var migCount = Regex.Matches(content, @"export\s+interface\s+MigrationDto").Count;
        Assert.True(migCount == 1, $"MigrationDto emitted {migCount} times, expected 1.");

        // PhasesDto same
        var phasesCount = Regex.Matches(content, @"export\s+interface\s+PhasesDto").Count;
        Assert.True(phasesCount == 1, $"PhasesDto emitted {phasesCount} times, expected 1.");
    }

    [Fact]
    public void FilePerClass_DiamondGraph_ExactlyOneFilePerType()
    {
        var model = BuildFullModel();
        var files = TypeScriptEmitter.Emit(model, new GlobalSettings());

        // 7 types: RegisterSolutionRequestDto, SolutionDependencyDto, SolutionVersionDto,
        // PhasesDto, MigrationDto, MigrationUpDto, MigrationDownDto
        var fileNames = files.Select(f => f.FileName).OrderBy(n => n).ToList();
        Assert.Equal(fileNames.Distinct().Count(), fileNames.Count);
        Assert.Single(files, f => f.FileName == "MigrationDownDto.ts");
    }

    // ── Full repro from user's actual type graph ──────────────────────────

    // Full repro: deep inheritance hierarchy + diamond reference + multiple collection
    // paths to the same type. Mirrors a real-world bug where a leaf DTO appeared 15x.
    private const string FullSource = """
        using System.Collections.Generic;
        using System.Text.Json.Nodes;
        using ZibStack.NET.TypeGen;

        namespace DeepDiamond;

        public enum ComponentKind { Rule, Widget, Layout }

        public interface IComponentRef
        {
            ComponentKind Kind { get; }
            string? ExternalId { get; init; }
        }

        public abstract record ComponentRef : IComponentRef
        {
            public abstract ComponentKind Kind { get; }
            public string? ExternalId { get; init; }
        }

        public record RuleRef : ComponentRef
        {
            public override ComponentKind Kind => ComponentKind.Rule;
            public string? Name { get; init; }
        }

        public record RuleItem : RuleRef
        {
            public JsonObject? Payload { get; init; }
            public bool? Active { get; set; }
        }

        public record WidgetItem : ComponentRef
        {
            public required string WidgetType { get; init; }
            public JsonObject? Payload { get; init; }
            public override ComponentKind Kind => ComponentKind.Widget;
            public bool? Active { get; set; }
        }

        public record LayoutRef : ComponentRef
        {
            public override ComponentKind Kind => ComponentKind.Layout;
            public string? EntityType { get; init; }
            public int? ViewId { get; init; }
        }

        public record LayoutItem : LayoutRef
        {
            public JsonObject? Payload { get; init; }
            public bool? Active { get; set; }
        }

        public record StepUpDto(string Action, object Data);
        public record StepDownDto(string Action, object Data);
        public record StepDto(
            string Name,
            StepUpDto Up,
            StepDownDto? Down,
            string? Description,
            bool? IsRepair = null);

        public record PipelineDto()
        {
            public required List<StepDto> Forward { get; init; } = [];
            public required List<StepDto> Rollback { get; init; } = [];
        }

        public record ReleaseDto(string Tag, PipelineDto Pipeline);
        public record DependencyDto(string PackageName, string MinVersion, int Priority = 0);

        public record InitialComponentsDto()
        {
            public RuleItem[] Rules { get; init; } = [];
            public WidgetItem[] Widgets { get; init; } = [];
            public LayoutItem[] Layouts { get; init; } = [];
        }

        [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = ".")]
        public record PackageRequestDto(
            string Name,
            string? Description,
            InitialComponentsDto Components,
            List<ReleaseDto> Releases,
            List<DependencyDto>? Dependencies);
        """;

    [Fact]
    public void FullTypeGraph_WithInheritanceAndDiamond_NoDuplicates()
    {
        var model = BuildFullModelFromSource(FullSource, "DeepDiamond.PackageRequestDto");

        var duplicates = model.Classes
            .GroupBy(c => c.CSharpFullName)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {g.Count()}x")
            .ToList();

        Assert.True(duplicates.Count == 0,
            $"Duplicate classes in model:\n{string.Join("\n", duplicates)}");
    }

    [Fact]
    public void FullTypeGraph_SingleFile_NoDuplicateDeclarations()
    {
        var model = BuildFullModelFromSource(FullSource, "DeepDiamond.PackageRequestDto");
        var settings = new GlobalSettings
        {
            TypeScript = { FileLayout = TypeScriptFileLayout.SingleFile, SingleFileName = "index.ts", OutputDir = "." },
        };
        var files = TypeScriptEmitter.Emit(model, settings);
        var content = Assert.Single(files).Content;

        // Check NO type is duplicated in the output
        var allDeclarations = Regex.Matches(content, @"export\s+(?:interface|type)\s+(\w+)");
        var declNames = allDeclarations.Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        var dupDecl = declNames.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {g.Count()}x").ToList();
        Assert.True(dupDecl.Count == 0,
            $"Duplicate declarations in output:\n{string.Join("\n", dupDecl)}\n\nFull output:\n{content}");

        // StepDownDto specifically — the diamond leaf that triggered the original report
        var downCount = Regex.Matches(content, @"export\s+interface\s+StepDownDto").Count;
        Assert.True(downCount == 1, $"StepDownDto emitted {downCount} times (expected 1).");
    }

    private static SchemaModel BuildFullModelFromSource(string source, string rootFqn)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(typeof(System.Text.Json.Nodes.JsonObject).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ZibStack.NET.TypeGen.TypeTarget).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create("DeepDiamondTest",
            new[] { tree }, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var diags = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(diags);

        var root = compilation.GetTypeByMetadataName(rootFqn);
        Assert.NotNull(root);

        var cls = SchemaParser.ParseClass(root!);
        Assert.NotNull(cls);

        var model = new SchemaModel();
        model.Classes.Add(cls!);
        SchemaParser.DiscoverTransitive(model, compilation);

        return model;
    }

    private static SchemaModel BuildFullModel()
    {
        var tree = CSharpSyntaxTree.ParseText(Source);
        var refs = new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
        };
        // Add TypeGen.Abstractions so [GenerateTypes] resolves
        var absPath = typeof(ZibStack.NET.TypeGen.TypeTarget).Assembly.Location;
        if (!string.IsNullOrEmpty(absPath))
            refs = refs.Append(MetadataReference.CreateFromFile(absPath)).ToArray();

        var compilation = CSharpCompilation.Create("DiamondTest",
            new[] { tree }, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var root = compilation.GetTypeByMetadataName("DiamondTest.RegisterSolutionRequestDto");
        Assert.NotNull(root);

        var cls = SchemaParser.ParseClass(root!);
        Assert.NotNull(cls);

        var model = new SchemaModel();
        model.Classes.Add(cls!);
        SchemaParser.DiscoverTransitive(model, compilation);

        return model;
    }
}
