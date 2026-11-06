using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using Xunit;

namespace TypeGenTests;

/// <summary>Real NuGet consumers: retain obj across builds and exercise the shipped
/// props/targets, including runs where analyzer-side live writes are unavailable.</summary>
public class EmptyManifestBuildTests : IClassFixture<TypeGenPackageFixture>
{
    private readonly TypeGenPackageFixture _package;
    public EmptyManifestBuildTests(TypeGenPackageFixture package) => _package = package;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RemovingLastRequest_ReplacesManifestAndCleansOwnedFiles(bool fluent, bool liveWrites)
    {
        var dir = _package.CreateConsumer(liveWrites);
        string Source(string output, bool example, bool other) =>
            "using ZibStack.NET.TypeGen;\n" +
            (!fluent && example ? $"[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.Zod, OutputDir = \"{output}\")]\n" : "") +
            "public class Example { public string Name { get; set; } = \"\"; }\n" +
            (!fluent && other ? $"[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.Zod, OutputDir = \"{output}\")]\n" : "") +
            "public class Other { public int Id { get; set; } }\n" +
            (fluent ? $$"""
                public class Config : ITypeGenConfigurator {
                    public void Configure(ITypeGenBuilder b) {
                        b.TypeScript(t => { t.OutputDir = "{{output}}"; });
                        b.Zod(z => { z.OutputDir = "{{output}}"; });
                        {{(example ? "b.ForType<Example>().WithGeneratedTypes(TypeTarget.TypeScript | TypeTarget.Zod);" : "")}}
                        {{(other ? "b.ForType<Other>().WithGeneratedTypes(TypeTarget.TypeScript | TypeTarget.Zod);" : "")}}
                    }
                }
                """ : "");

        async Task Build(string? source = null, bool expectEmptyLiveOutput = false)
        {
            if (source != null) await File.WriteAllTextAsync(Path.Combine(dir, "Models.cs"), source);
            await TypeGenPackageFixture.Run("build Consumer.csproj --nologo" +
                (expectEmptyLiveOutput ? " -p:ExpectEmptyLiveOutput=true" : ""), dir);
        }
        string FileAt(string output, string file) => Path.Combine(dir, output, file);
        await Build(Source("generated", true, true));
        var editorConfig = Assert.Single(Directory.GetFiles(Path.Combine(dir, "obj"), "*.GeneratedMSBuildEditorConfig.editorconfig", SearchOption.AllDirectories));
        Assert.Equal(liveWrites, File.ReadAllText(editorConfig).Contains("build_property.MSBuildProjectDirectory ="));
        var original = File.ReadAllText(FileAt("generated", "Example.ts"));
        Assert.True(File.Exists(FileAt("generated", "Example.schema.ts")));
        var timestamp = File.GetLastWriteTimeUtc(FileAt("generated", "Example.ts"));
        // Force compilation while keeping generator output identical.
        await Build(Source("generated", true, true) + "\n// unchanged output\n");
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(FileAt("generated", "Example.ts")));
        await File.WriteAllTextAsync(FileAt("generated", "handwritten.ts"), "export const keep = true;");

        await Build(Source("renamed", true, true));
        Assert.False(File.Exists(FileAt("generated", "Example.ts")));
        Assert.True(File.Exists(FileAt("generated", "handwritten.ts")));
        await File.WriteAllTextAsync(FileAt("renamed", "handwritten.ts"), "export const keep = true;");
        await Build(Source("renamed", false, true));
        Assert.False(File.Exists(FileAt("renamed", "Example.ts")));
        Assert.True(File.Exists(FileAt("renamed", "Other.ts")));
        Assert.True(File.Exists(FileAt("renamed", "Other.schema.ts")));

        // Reproduce deleting output while retaining obj before the empty run.
        File.Delete(FileAt("renamed", "Other.schema.ts"));
        await Build(Source("renamed", false, false), expectEmptyLiveOutput: liveWrites);
        var manifest = Assert.Single(Directory.GetFiles(Path.Combine(dir, "obj"), "ZibStack.TypeGen.Manifest.g.cs", SearchOption.AllDirectories));
        Assert.Contains("FileCount = 0;", File.ReadAllText(manifest));
        Assert.False(File.Exists(FileAt("renamed", "Other.ts")));
        Assert.False(File.Exists(FileAt("renamed", "Other.schema.ts")));
        Assert.True(File.Exists(FileAt("renamed", "handwritten.ts")));

        // The empty manifest must also drive MSBuild's cleanup on a skipped
        // compilation (live writes cannot mask that path).
        await File.WriteAllTextAsync(FileAt("generated", "Example.ts"), original);
        await Build();
        Assert.False(File.Exists(FileAt("generated", "Example.ts")));
        await Build();
        Assert.False(File.Exists(FileAt("renamed", "Other.ts")));
        Assert.True(File.Exists(FileAt("generated", "handwritten.ts")));
        Assert.True(File.Exists(FileAt("renamed", "handwritten.ts")));
    }

    [Fact]
    public async Task EmptyEmitterResult_CleansOutput_ButFailedGenerationDoesNot()
    {
        var dir = _package.CreateConsumer(liveWrites: true);
        var sourcePath = Path.Combine(dir, "Models.cs");
        const string initial = """
            using ZibStack.NET.TypeGen;
            [GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.Zod, OutputDir = "generated")]
            public class Example { public string Name { get; set; } = ""; }
            """;
        await File.WriteAllTextAsync(sourcePath, initial);
        await TypeGenPackageFixture.Run("build --nologo", dir);
        var output = Path.Combine(dir, "generated", "Example.ts");
        var original = File.ReadAllText(output);
        var manifest = Assert.Single(Directory.GetFiles(Path.Combine(dir, "obj"), "ZibStack.TypeGen.Manifest.g.cs", SearchOption.AllDirectories));
        var originalManifest = File.ReadAllText(manifest);
        foreach (var invalid in new[]
        {
            // No discovered model due to a temporarily broken compilation.
            "public class Broken {",
            // Parser error with no model; must not masquerade as an empty run.
            """
            using ZibStack.NET.TypeGen;
            public class First : ITypeGenConfigurator { public void Configure(ITypeGenBuilder b) {} }
            public class Second : ITypeGenConfigurator { public void Configure(ITypeGenBuilder b) {} }
            """,
            // Generator rejects the only requested type.
            """
            using ZibStack.NET.TypeGen;
            [GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = "generated")]
            public class Unsupported<T> { }
            """,
            // Existing fatal Zod validation path must continue to preserve output.
            """
            using ZibStack.NET.TypeGen;
            [GenerateTypes(Targets = TypeTarget.Zod, OutputDir = "generated")]
            public class Example {
                [ZodSchema("z.string()", ImportFrom = "zod")]
                public string Name { get; set; } = "";
            }
            """,
        })
        {
            await File.WriteAllTextAsync(sourcePath, invalid);
            await TypeGenPackageFixture.Run("build --nologo", dir, success: false);
            Assert.Equal(original, File.ReadAllText(output));
            Assert.Equal(originalManifest, File.ReadAllText(manifest));
        }

        // A model exists, but the only requested emitter has no endpoints.
        // This covers the second former early-return (allFiles.Count == 0).
        await File.WriteAllTextAsync(sourcePath, """
            using ZibStack.NET.TypeGen;
            [GenerateTypes(Targets = TypeTarget.TanStackQuery, OutputDir = "generated")]
            public class Example { public string Name { get; set; } = ""; }
            """);
        await TypeGenPackageFixture.Run("build --nologo", dir);
        Assert.Contains("FileCount = 0;", File.ReadAllText(manifest));
        Assert.False(File.Exists(output));
        Assert.False(File.Exists(Path.Combine(dir, "generated", "Example.schema.ts")));
    }
}

public sealed class TypeGenPackageFixture : IDisposable
{
    private const string Version = "0.0.0-empty-manifest-tests";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "typegen-package-" + Guid.NewGuid().ToString("N"));
    public TypeGenPackageFixture()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo != null && !File.Exists(Path.Combine(repo.FullName, "ZibStack.NET.slnx"))) repo = repo.Parent;
        Assert.NotNull(repo);
        Directory.CreateDirectory(Path.Combine(_root, "feed"));
        var src = Path.Combine(repo!.FullName, "packages", "ZibStack.NET.TypeGen", "src");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        foreach (var name in new[] { "ZibStack.NET.TypeGen.Abstractions", "ZibStack.NET.TypeGen" })
            Run($"pack \"{Path.Combine(src, name, name + ".csproj")}\" -c {configuration} --no-build -p:PackageVersion={Version} -o \"{Path.Combine(_root, "feed")}\"", repo.FullName).GetAwaiter().GetResult();
    }

    public string CreateConsumer(bool liveWrites)
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Consumer.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <UseSharedCompilation>false</UseSharedCompilation>
                <RestoreSources>{{SecurityElement.Escape(Path.Combine(_root, "feed"))}}</RestoreSources>
                <RestorePackagesPath>{{SecurityElement.Escape(Path.Combine(_root, "packages"))}}</RestorePackagesPath>
              </PropertyGroup>
              <ItemGroup><PackageReference Include="ZibStack.NET.TypeGen" Version="{{Version}}" /></ItemGroup>
              <Target Name="DisableLiveWrites" BeforeTargets="GenerateMSBuildEditorConfigFileCore" Condition="'{{liveWrites}}' == 'False'">
                <ItemGroup><CompilerVisibleProperty Remove="MSBuildProjectDirectory" /></ItemGroup>
              </Target>
              <Target Name="VerifyLiveCleanup" BeforeTargets="ZibStackTypeGenWriteOutputFiles" Condition="'$(ExpectEmptyLiveOutput)' == 'true'">
                <Error Condition="Exists('renamed/Other.ts') Or Exists('renamed/Other.schema.ts')" Text="Live writes must clean before MSBuild's sweep." />
              </Target>
            </Project>
            """);
        return dir;
    }

    public static async Task<string> Run(string args, string dir, bool success = true)
    {
        var start = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        var output = await stdout + await stderr;
        Assert.True((process.ExitCode == 0) == success, $"dotnet {args}\n{output}");
        return output;
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }
}
