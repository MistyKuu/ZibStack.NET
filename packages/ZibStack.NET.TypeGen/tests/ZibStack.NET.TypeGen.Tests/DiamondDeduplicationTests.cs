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
