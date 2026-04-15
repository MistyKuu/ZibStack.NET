using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace TypeGenTests;

/// <summary>
/// End-to-end sanity: runs <c>dotnet build</c> on the in-repo SampleApi and asserts
/// that the full pipeline (source generator → manifest .g.cs → RoslynCodeTaskFactory
/// MSBuild task) writes the expected .ts / .yaml files to the configured OutputDir.
///
/// <para>
/// The unit/integration tests in this assembly exercise the emitters in isolation.
/// This test is the only one that covers the MSBuild glue — the bit that's hardest
/// to get right and easiest to break silently.
/// </para>
/// </summary>
public sealed class SampleApiBuildTests
{
    private static readonly string RepoRoot = LocateRepoRoot();
    private static readonly string SampleProj = Path.Combine(
        RepoRoot, "packages", "ZibStack.NET.TypeGen", "sample", "SampleApi", "SampleApi.csproj");
    private static readonly string GeneratedDir = Path.Combine(
        RepoRoot, "packages", "ZibStack.NET.TypeGen", "sample", "SampleApi", "generated");

    [Fact]
    public void SampleApi_Build_WritesExpectedFilesToGeneratedDir()
    {
        // Wipe first so we're sure the build is what populated the directory.
        if (Directory.Exists(GeneratedDir))
            foreach (var f in Directory.EnumerateFiles(GeneratedDir))
                File.Delete(f);

        var (exit, output) = Run("dotnet", $"build \"{SampleProj}\" -c Release --nologo");
        Assert.True(exit == 0, $"dotnet build failed:\n{output}");

        // All four artifacts must land under generated/, nothing in the project root.
        Assert.True(File.Exists(Path.Combine(GeneratedDir, "Order.ts")));
        Assert.True(File.Exists(Path.Combine(GeneratedDir, "OrderItem.ts")));
        Assert.True(File.Exists(Path.Combine(GeneratedDir, "OrderStatus.ts")));
        Assert.True(File.Exists(Path.Combine(GeneratedDir, "openapi.yaml")));

        // The root-level openapi.yaml bug (pre-OutputDir-routing fix) would leave
        // a copy next to SampleApi.csproj. Guard against regression.
        var rootStray = Path.Combine(Path.GetDirectoryName(SampleProj)!, "openapi.yaml");
        Assert.False(File.Exists(rootStray), "openapi.yaml leaked to project root — OutputDir routing broken.");

        // Cross-file imports must actually be present — the other regression we just fixed.
        var orderTs = File.ReadAllText(Path.Combine(GeneratedDir, "Order.ts"));
        Assert.Contains("import { OrderItem } from './OrderItem';", orderTs);
        Assert.Contains("import { OrderStatus } from './OrderStatus';", orderTs);

        // OpenAPI version must be 3.0-compatible for Microsoft.OpenApi.Readers.
        var yaml = File.ReadAllText(Path.Combine(GeneratedDir, "openapi.yaml"));
        Assert.Contains("openapi: 3.0.3", yaml);

        // Fluent configurator in SampleApi/TypeGenConfig.cs overrides Title/Version/Description.
        // If these don't match, ConfiguratorParser didn't run end-to-end through the build.
        Assert.Contains("title: Sample Order API", yaml);
        Assert.Contains("version: 1.2.3", yaml);
        Assert.Contains("description: Demo service exercising the TypeGen fluent configurator.", yaml);
    }

    private static (int Exit, string Output) Run(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout + stderr);
    }

    private static string LocateRepoRoot()
    {
        // Walk up from the test assembly until we find the .slnx — simpler than
        // threading a build constant through and works regardless of bin layout.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.EnumerateFiles("ZibStack.NET.slnx").Any())
            dir = dir.Parent;
        if (dir == null) throw new InvalidOperationException("Could not locate ZibStack.NET.slnx walking up from test binary.");
        return dir.FullName;
    }
}
