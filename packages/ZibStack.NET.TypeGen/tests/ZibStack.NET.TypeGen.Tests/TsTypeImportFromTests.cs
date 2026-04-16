using System.Linq;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// <c>[TsType("Foo", ImportFrom = "./bar")]</c> tells the TS emitter to add an
/// <c>import { Foo } from './bar';</c> line so the override-typed property
/// actually compiles. Multiple identifiers in the type expression
/// (<c>"Map&lt;Foo, Bar&gt;"</c>) all get pulled from the same path.
/// </summary>
public class TsTypeImportFromTests
{
    private static SchemaClass Cls(string name, params (string Name, string Type, string? TsType, string? ImportFrom)[] props)
    {
        var c = new SchemaClass
        {
            CSharpFullName = name, SourceName = name, EmittedName = name,
            OutputDir = ".", Targets = TypeTarget.TypeScript,
        };
        foreach (var (n, t, tsType, imp) in props)
            c.Properties.Add(new SchemaProperty
            {
                SourceName = n, CSharpTypeFullName = t, IsNullable = true,
                TsTypeOverride = tsType, TsImportFrom = imp,
            });
        return c;
    }

    private static SchemaModel ModelWith(params SchemaClass[] classes)
    {
        var m = new SchemaModel();
        m.Classes.AddRange(classes);
        return m;
    }

    [Fact]
    public void ImportFrom_Single_EmitsImportLine()
    {
        var cls = Cls("Rule",
            ("Element", "System.Text.Json.Nodes.JsonObject", "AutomationRulePayload", "./types/automation-rule-payload"));
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).First(f => f.FileName == "Rule.ts").Content;

        Assert.Contains("import { AutomationRulePayload } from './types/automation-rule-payload';", ts);
        Assert.Contains("element?: AutomationRulePayload;", ts);
    }

    [Fact]
    public void ImportFrom_MultiplePascalIdentifiers_MergeIntoOneImport()
    {
        var cls = Cls("Wrapper",
            ("Item", "System.Object", "Map<Foo, Bar>", "./types/api"));
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).First(f => f.FileName == "Wrapper.ts").Content;

        // Map, Foo, Bar — all PascalCase identifiers in the expression — should land in the same import.
        Assert.Matches(@"import \{ Bar, Foo, Map \} from './types/api';", ts);
        Assert.Contains("item?: Map<Foo, Bar>;", ts);
    }

    [Fact]
    public void ImportFrom_TwoPropertiesSamePath_DedupedToOneImport()
    {
        var cls = Cls("R",
            ("A", "System.Object", "Foo", "./types"),
            ("B", "System.Object", "Bar", "./types"));
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).First(f => f.FileName == "R.ts").Content;

        Assert.Matches(@"import \{ Bar, Foo \} from './types';", ts);
        // Only one import line for './types' — make sure we didn't emit two
        var importLines = ts.Split('\n').Count(l => l.Contains("from './types'"));
        Assert.Equal(1, importLines);
    }

    [Fact]
    public void ImportFrom_DifferentPaths_EmittedSeparately()
    {
        var cls = Cls("R",
            ("A", "System.Object", "Foo", "./a"),
            ("B", "System.Object", "Bar", "./b"));
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).First(f => f.FileName == "R.ts").Content;

        Assert.Contains("import { Foo } from './a';", ts);
        Assert.Contains("import { Bar } from './b';", ts);
    }

    [Fact]
    public void ImportFrom_Null_NoImportEmitted()
    {
        // TsType without ImportFrom — primitive, literal union, etc. No import.
        var cls = Cls("R",
            ("S", "System.Guid", "string", null),
            ("U", "System.Object", "'a' | 'b'", null));
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).First(f => f.FileName == "R.ts").Content;

        Assert.DoesNotContain("import {", ts);
        Assert.Contains("s?: string;", ts);
        Assert.Contains("u?: 'a' | 'b';", ts);
    }

    [Fact]
    public void ImportFrom_PrimitivesInExpression_AreSkipped()
    {
        // string and number are TS primitives, not user types — should NOT appear in imports.
        var cls = Cls("R",
            ("X", "System.Object", "Either<Foo, string>", "./either"));
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).First(f => f.FileName == "R.ts").Content;

        // Either + Foo are imported; lowercase "string" stays a primitive.
        Assert.Matches(@"import \{ Either, Foo \} from './either';", ts);
        Assert.DoesNotContain("string }", ts.Substring(0, ts.IndexOf("export ")));
    }

    [Fact]
    public void ImportFrom_SingleFileLayout_RolledIntoTopOfFile()
    {
        var a = Cls("A", ("X", "System.Object", "Foo", "./shared"));
        var b = Cls("B", ("Y", "System.Object", "Bar", "./shared"));
        var settings = new GlobalSettings();
        settings.TypeScript.FileLayout = TypeScriptFileLayout.SingleFile;

        var ts = TypeScriptEmitter.Emit(ModelWith(a, b), settings).Single().Content;

        // One merged import covering both classes — same path, both symbols.
        Assert.Matches(@"import \{ Bar, Foo \} from './shared';", ts);
    }
}
