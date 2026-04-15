using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// Round-trips PythonEmitter output through the real CPython interpreter with
/// Pydantic v2 installed: writes the modules to a temp dir and runs
/// <c>python -c "import order"</c> (and similar). If the generated code has a
/// syntax error, missing import, or wrong Pydantic shape, Python exits non-zero
/// and the test fails with the actual interpreter output.
///
/// <para>
/// Skipped when Python isn't on PATH (CI images sometimes lack it). When present,
/// Pydantic must already be installed in the environment — bootstrap with
/// <c>python -m pip install pydantic</c>.
/// </para>
/// </summary>
public sealed class PythonCompilationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _python;

    public PythonCompilationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "zibstack-typegen-py-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _python = LocatePython();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private static SchemaClass Cls(string name, params (string Name, string CSharpType, bool Nullable)[] props)
    {
        var c = new SchemaClass
        {
            CSharpFullName = name, SourceName = name, EmittedName = name,
            OutputDir = ".", Targets = TypeTarget.Python,
        };
        foreach (var (n, t, nu) in props)
            c.Properties.Add(new SchemaProperty { SourceName = n, CSharpTypeFullName = t, IsNullable = nu });
        return c;
    }

    [Fact]
    public void PydanticOutput_ImportsAndInstantiates()
    {
        if (_python is null) return;   // Python missing → silent skip

        var model = new SchemaModel();
        model.Classes.Add(Cls("Customer",
            ("Id", "int", false),
            ("Name", "string", false),
            ("Email", "string", true)));

        WriteFiles(PythonEmitter.Emit(model, new GlobalSettings()));

        // Real Pydantic round-trip: parse JSON with C# names, access via snake_case.
        var script =
            "import sys, json\n" +
            "sys.path.insert(0, r'" + _tempDir + "')\n" +
            "from customer import Customer\n" +
            "c = Customer.model_validate({'Id': 1, 'Name': 'Alice', 'Email': None})\n" +
            "assert c.id == 1\n" +
            "assert c.name == 'Alice'\n" +
            "assert c.email is None\n" +
            "roundtrip = json.loads(c.model_dump_json(by_alias=True))\n" +
            "assert roundtrip == {'Id': 1, 'Name': 'Alice', 'Email': None}\n" +
            "print('OK')\n";

        var (exit, output) = RunScript(_python, script);
        Assert.True(exit == 0, $"Pydantic round-trip failed:\n{output}");
        Assert.Contains("OK", output);
    }

    [Fact]
    public void EnumOutput_ImportsAsIntEnum()
    {
        if (_python is null) return;

        var model = new SchemaModel();
        var en = new SchemaEnum
        {
            CSharpFullName = "OrderStatus", SourceName = "OrderStatus",
            EmittedName = "OrderStatus", Targets = TypeTarget.Python, OutputDir = ".",
        };
        en.Members.Add(new SchemaEnumMember { Name = "Pending", Value = 0 });
        en.Members.Add(new SchemaEnumMember { Name = "Shipped", Value = 1 });
        en.Members.Add(new SchemaEnumMember { Name = "Delivered", Value = 2 });
        model.Enums.Add(en);

        WriteFiles(PythonEmitter.Emit(model, new GlobalSettings()));

        var script =
            "import sys\n" +
            "sys.path.insert(0, r'" + _tempDir + "')\n" +
            "from order_status import OrderStatus\n" +
            "assert OrderStatus.PENDING == 0\n" +
            "assert OrderStatus.SHIPPED == 1\n" +
            "assert int(OrderStatus.DELIVERED) == 2\n" +
            "print('OK')\n";

        var (exit, output) = RunScript(_python, script);
        Assert.True(exit == 0, $"Enum import failed:\n{output}");
        Assert.Contains("OK", output);
    }

    [Fact]
    public void CrossClassRefs_ImportResolvesAndComposes()
    {
        if (_python is null) return;

        var model = new SchemaModel();
        model.Classes.Add(Cls("OrderItem", ("Sku", "string", false), ("Qty", "int", false)));
        model.Classes.Add(Cls("Order",
            ("Id", "int", false),
            ("Items", "System.Collections.Generic.List<OrderItem>", false)));

        WriteFiles(PythonEmitter.Emit(model, new GlobalSettings()));

        var script =
            "import sys\n" +
            "sys.path.insert(0, r'" + _tempDir + "')\n" +
            "from order import Order\n" +
            "o = Order.model_validate({'Id': 1, 'Items': [{'Sku': 'A', 'Qty': 2}]})\n" +
            "assert o.id == 1\n" +
            "assert len(o.items) == 1\n" +
            "assert o.items[0].sku == 'A'\n" +
            "print('OK')\n";

        var (exit, output) = RunScript(_python, script);
        Assert.True(exit == 0, $"Cross-class ref failed:\n{output}");
        Assert.Contains("OK", output);
    }

    /// <summary>
    /// Writes the script to a temp .py file and runs it — avoids -c escaping
    /// pain for multi-line scripts with quotes and braces.
    /// </summary>
    private (int Exit, string Output) RunScript(string python, string script)
    {
        var scriptPath = Path.Combine(_tempDir, $"_test_{Guid.NewGuid():N}.py");
        File.WriteAllText(scriptPath, script);
        return Run(python, $"\"{scriptPath}\"");
    }

    private void WriteFiles(System.Collections.Generic.IReadOnlyList<EmittedFile> files)
    {
        foreach (var f in files)
            File.WriteAllText(Path.Combine(_tempDir, f.FileName), f.Content);
    }

    private static (int Exit, string Output) Run(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout + stderr);
    }

    /// <summary>
    /// Locates the Python interpreter — checks <c>python</c>, <c>python3</c>, then
    /// the standard Windows install path. Returns null when nothing works so tests
    /// silently skip on CI machines without Python.
    /// </summary>
    private static string? LocatePython()
    {
        foreach (var candidate in new[] { "python", "python3" })
        {
            try
            {
                var psi = new ProcessStartInfo(candidate, "--version")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p is null) continue;
                p.WaitForExit(2000);
                if (p.ExitCode == 0) return candidate;
            }
            catch { /* try next */ }
        }
        // Windows-specific fallback (winget installs here).
        var winInstall = Environment.ExpandEnvironmentVariables(
            @"%LOCALAPPDATA%\Programs\Python\Python312\python.exe");
        if (File.Exists(winInstall)) return winInstall;
        return null;
    }
}
