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

        // All artifacts must land under generated/, nothing in the project root.
        // OrderItem is renamed to "hoho" via b.ForType<OrderItem>().TsName("hoho") in TypeGenConfig.cs.
        Assert.True(File.Exists(Path.Combine(GeneratedDir, "Order.ts")));
        Assert.True(File.Exists(Path.Combine(GeneratedDir, "hoho.ts")));
        Assert.True(File.Exists(Path.Combine(GeneratedDir, "OrderStatus.ts")));
        Assert.True(File.Exists(Path.Combine(GeneratedDir, "openapi.yaml")));

        // The root-level openapi.yaml bug (pre-OutputDir-routing fix) would leave
        // a copy next to SampleApi.csproj. Guard against regression.
        var rootStray = Path.Combine(Path.GetDirectoryName(SampleProj)!, "openapi.yaml");
        Assert.False(File.Exists(rootStray), "openapi.yaml leaked to project root — OutputDir routing broken.");

        // Cross-file imports must follow the renamed type — proves the import resolver
        // walks the post-fluent emitted name, not the source name.
        var orderTs = File.ReadAllText(Path.Combine(GeneratedDir, "Order.ts"));
        Assert.Contains("import { hoho } from './hoho';", orderTs);
        Assert.Contains("import { OrderStatus } from './OrderStatus';", orderTs);

        // Per-property fluent on the renamed class: UnitPrice -> ASD via
        // b.ForType<OrderItem>().Property(x => x.UnitPrice).TsName("ASD").
        var hohoTs = File.ReadAllText(Path.Combine(GeneratedDir, "hoho.ts"));
        Assert.Contains("ASD: string;", hohoTs);
        Assert.DoesNotContain("unitPrice", hohoTs);

        // OpenAPI version must be 3.0-compatible for Microsoft.OpenApi.Readers.
        var yaml = File.ReadAllText(Path.Combine(GeneratedDir, "openapi.yaml"));
        Assert.Contains("openapi: 3.0.3", yaml);

        // Fluent configurator in SampleApi/TypeGenConfig.cs overrides Title/Version/Description.
        // If these don't match, ConfiguratorParser didn't run end-to-end through the build.
        Assert.Contains("title: Sample Order API", yaml);
        Assert.Contains("version: 1.2.3", yaml);
        Assert.Contains("description: Demo service exercising the TypeGen fluent configurator.", yaml);

        // Per-type fluent override: b.ForType<Customer>().TsName("CustomerDto").OpenApiName("CustomerV1").
        // Customer.cs has [GenerateTypes] only — no per-class rename attribute — so this proves
        // the per-type fluent path actually applies to the schema/file emission.
        Assert.True(File.Exists(Path.Combine(GeneratedDir, "CustomerDto.ts")),
            "TS per-type rename via b.ForType<Customer>().TsName(\"CustomerDto\") didn't take effect.");
        Assert.False(File.Exists(Path.Combine(GeneratedDir, "Customer.ts")),
            "Old non-renamed Customer.ts still present — fluent rename didn't supersede source name.");
        Assert.Contains("    CustomerV1:", yaml);

        // Per-property fluent: b.ForType<Customer>().Property(c => c.Email).TsName("emailAddress")
        // .OpenApiFormat("email").OpenApiDescription("Verified contact email.")
        var customerTs = File.ReadAllText(Path.Combine(GeneratedDir, "CustomerDto.ts"));
        Assert.Contains("emailAddress: string;", customerTs);
        Assert.DoesNotContain("email:", customerTs);   // original camelCase name must not leak
        Assert.Contains("format: email", yaml);
        Assert.Contains("description: Verified contact email.", yaml);
        Assert.Contains("description: Display name shown in receipts.", yaml);

        // [CrudApi] on Order contributes a full paths block — full RESTful endpoint set.
        Assert.Contains("  /api/orders:", yaml);
        Assert.Contains("  /api/orders/{id}:", yaml);
        Assert.Contains("operationId: createOrder", yaml);
        Assert.Contains("operationId: getOrderById", yaml);
        Assert.Contains("operationId: updateOrder", yaml);
        Assert.Contains("operationId: deleteOrder", yaml);
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
