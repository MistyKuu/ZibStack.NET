using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// Integration test: run the generated Zod schemas through a real <c>tsc</c> +
/// <c>zod</c> install. If the emitted <c>.schema.ts</c> files don't type-check
/// (wrong method signature, missing member, arity mismatch on <c>z.discriminatedUnion</c>,
/// etc.), tsc surfaces the error and the test fails with actionable output.
///
/// <para>
/// Skipped when Node isn't available — matches the TS compilation tests.
/// </para>
/// </summary>
public sealed class ZodCompilationTests : IDisposable
{
    // Pin both packages for determinism across machines / CI.
    private const string TscPackageSpec = "typescript@5.7.3";
    private const string ZodPackageSpec = "zod@3.23.8";

    private readonly string _tempDir;
    private readonly bool _skip;

    public ZodCompilationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "zibstack-zod-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _skip = !NodeAvailable();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task EmittedOutput_FromCrossReferencedModel_CompilesWithTsc()
    {
        if (_skip) return;

        var model = new SchemaModel();
        model.Classes.Add(ClsModel("Order", new[]
        {
            ("Id", "int", false),
            ("Customer", "string", false),
            ("Status", "OrderStatus", false),
            ("Items", "System.Collections.Generic.List<OrderItem>", false),
            ("Note", "string", true),
        }));
        model.Classes.Add(ClsModel("OrderItem", new[]
        {
            ("Sku", "string", false),
            ("Quantity", "int", false),
        }));
        var en = new SchemaEnum
        {
            CSharpFullName = "OrderStatus", SourceName = "OrderStatus",
            EmittedName = "OrderStatus", Targets = TypeTarget.Zod, OutputDir = ".",
        };
        en.Members.Add(new SchemaEnumMember { Name = "Pending", Value = 0 });
        en.Members.Add(new SchemaEnumMember { Name = "Shipped", Value = 1 });
        model.Enums.Add(en);

        var files = ZodEmitter.Emit(model, new GlobalSettings());
        await PrepareWorkspaceAsync();
        foreach (var f in files)
            File.WriteAllText(Path.Combine(_tempDir, f.FileName), f.Content);

        var (exitCode, stdout, stderr) = await RunAsync(
            "npx",
            $"-y -p {TscPackageSpec} tsc --noEmit --strict --target ES2020 --moduleResolution node " +
                string.Join(" ", files.Select(f => f.FileName)),
            workingDir: _tempDir);

        if (exitCode != 0)
        {
            var dump = new System.Text.StringBuilder();
            dump.AppendLine($"tsc exited {exitCode}.");
            dump.AppendLine($"stdout:{Environment.NewLine}{stdout}");
            dump.AppendLine($"stderr:{Environment.NewLine}{stderr}");
            foreach (var f in files)
            {
                dump.AppendLine($"── {f.FileName} ──");
                dump.AppendLine(f.Content);
            }
            Assert.Fail(dump.ToString());
        }
    }

    [Fact]
    public async Task PolymorphicUnion_ProducesValidDiscriminatedUnion()
    {
        if (_skip) return;

        var model = new SchemaModel();

        var baseCls = ClsModel("Shape", System.Array.Empty<(string, string, bool)>());
        baseCls.PolymorphicDiscriminator = "kind";
        baseCls.PolymorphicVariants.Add(new PolymorphicVariant { CSharpFullName = "Circle", DiscriminatorValue = "circle" });
        baseCls.PolymorphicVariants.Add(new PolymorphicVariant { CSharpFullName = "Square", DiscriminatorValue = "square" });
        model.Classes.Add(baseCls);

        var circle = ClsModel("Circle", new[] { ("Radius", "double", false) });
        circle.PolymorphicDiscriminatorValue = "circle";
        circle.PolymorphicDiscriminatorPropertyOnVariant = "kind";
        model.Classes.Add(circle);

        var square = ClsModel("Square", new[] { ("Side", "double", false) });
        square.PolymorphicDiscriminatorValue = "square";
        square.PolymorphicDiscriminatorPropertyOnVariant = "kind";
        model.Classes.Add(square);

        var files = ZodEmitter.Emit(model, new GlobalSettings());
        await PrepareWorkspaceAsync();
        foreach (var f in files)
            File.WriteAllText(Path.Combine(_tempDir, f.FileName), f.Content);

        var (exitCode, stdout, stderr) = await RunAsync(
            "npx",
            $"-y -p {TscPackageSpec} tsc --noEmit --strict --target ES2020 --moduleResolution node " +
                string.Join(" ", files.Select(f => f.FileName)),
            workingDir: _tempDir);

        Assert.True(exitCode == 0,
            $"tsc failed (exit {exitCode}):{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task PrepareWorkspaceAsync()
    {
        // Install zod locally so the emitted `import { z } from 'zod';` resolves.
        // Node resolution walks up from temp dir; a sibling node_modules is enough.
        var (code, _, err) = await RunAsync("npx",
            $"-y -p npm@10 npm install --silent --no-audit --no-fund --no-package-lock {ZodPackageSpec}",
            workingDir: _tempDir);
        if (code != 0)
            throw new InvalidOperationException($"zod install failed (exit {code}): {err}");
    }

    private static SchemaClass ClsModel(string name, (string Name, string CSharpType, bool Nullable)[] props)
    {
        var c = new SchemaClass
        {
            CSharpFullName = name, SourceName = name, EmittedName = name,
            OutputDir = ".", Targets = TypeTarget.Zod,
        };
        foreach (var (n, t, nu) in props)
            c.Properties.Add(new SchemaProperty { SourceName = n, CSharpTypeFullName = t, IsNullable = nu });
        return c;
    }

    private static bool NodeAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("node", "--version")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string fileName, string args, string workingDir)
    {
        string exe = fileName;
        string effectiveArgs = args;
        if (OperatingSystem.IsWindows() && fileName == "npx")
        {
            exe = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            effectiveArgs = $"/c npx {args}";
        }

        var psi = new ProcessStartInfo(exe, effectiveArgs)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        await Task.Run(() => p.WaitForExit(180_000));
        return (p.ExitCode, await stdoutTask, await stderrTask);
    }
}
