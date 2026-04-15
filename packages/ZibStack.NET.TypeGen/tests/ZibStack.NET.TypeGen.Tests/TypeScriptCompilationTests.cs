using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// Integration tests that take the generator's output and run it through the real
/// TypeScript compiler (<c>npx -y typescript tsc --noEmit</c>). If the generated
/// <c>.ts</c> files don't compile cleanly, <c>tsc</c> exits non-zero and the test
/// fails with the actual compiler output — a much stronger signal than
/// string-match assertions in the unit tests.
///
/// <para>
/// Skipped if Node / npx isn't installed (CI images sometimes lack it). Locally
/// the tests run against whatever TypeScript version npx resolves — pin it via
/// the package spec below when this becomes flaky across tsc versions.
/// </para>
/// </summary>
public sealed class TypeScriptCompilationTests : IDisposable
{
    // Pinned tsc version so the test is deterministic across machines.
    private const string TscPackageSpec = "typescript@5.7.3";

    private readonly string _tempDir;
    private readonly bool _skip;

    public TypeScriptCompilationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "zibstack-typegen-tests", Guid.NewGuid().ToString("N"));
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
        // Xunit has no first-class skip; silently pass when Node isn't installed
        // (some CI images lack it). Local runs get the real assertion.
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
            EmittedName = "OrderStatus", Targets = TypeTarget.TypeScript, OutputDir = ".",
        };
        en.Members.Add(new SchemaEnumMember { Name = "Pending", Value = 0 });
        en.Members.Add(new SchemaEnumMember { Name = "Shipped", Value = 1 });
        model.Enums.Add(en);

        var files = TypeScriptEmitter.Emit(model, new GlobalSettings());
        foreach (var f in files)
            File.WriteAllText(Path.Combine(_tempDir, f.FileName), f.Content);

        // Sanity dump of generated files in failure message.
        var fileList = string.Join(", ", files.Select(f => f.FileName));
        Assert.True(files.Count >= 2, $"expected multiple files, got {fileList}");

        var (exitCode, stdout, stderr) = await RunAsync(
            "npx",
            $"-y -p {TscPackageSpec} tsc --noEmit --strict --target ES2020 --moduleResolution node " +
                string.Join(" ", files.Select(f => f.FileName)),
            workingDir: _tempDir);

        if (exitCode != 0)
        {
            // Attach generated TS so a failing test is actionable.
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
    public async Task EmittedOutput_StandaloneClass_CompilesWithTsc()
    {
        if (_skip) return;

        var model = new SchemaModel();
        model.Classes.Add(ClsModel("Widget", new[]
        {
            ("Id", "System.Guid", false),
            ("Created", "System.DateTime", true),
            ("Price", "decimal", false),
            ("Active", "bool", false),
        }));

        var files = TypeScriptEmitter.Emit(model, new GlobalSettings());
        foreach (var f in files)
            File.WriteAllText(Path.Combine(_tempDir, f.FileName), f.Content);

        var (exitCode, stdout, stderr) = await RunAsync(
            "npx",
            $"-y -p {TscPackageSpec} tsc --noEmit --strict --target ES2020 --moduleResolution node " +
                string.Join(" ", files.Select(f => f.FileName)),
            workingDir: _tempDir);

        Assert.True(exitCode == 0, $"tsc failed (exit {exitCode}): {stdout}{Environment.NewLine}{stderr}");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static SchemaClass ClsModel(string name, (string Name, string CSharpType, bool Nullable)[] props)
    {
        var c = new SchemaClass
        {
            CSharpFullName = name, SourceName = name, EmittedName = name,
            OutputDir = ".", Targets = TypeTarget.TypeScript,
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
        // npx on Windows is a .cmd shim — ProcessStartInfo needs cmd.exe to resolve it.
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
        // Read streams concurrently so the process doesn't block on a full pipe.
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        await Task.Run(() => p.WaitForExit(120_000));
        return (p.ExitCode, await stdoutTask, await stderrTask);
    }
}

