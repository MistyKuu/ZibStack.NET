using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// Computed / immutable property support across the three targets.
///
/// <para>
/// C# setter shape drives the model:
/// <list type="bullet">
///   <item><c>=&gt; expr;</c> and <c>{ get; }</c> → <c>IsReadOnly = true</c></item>
///   <item><c>{ get; private set; }</c> → <c>IsReadOnly = true</c></item>
///   <item><c>{ get; init; }</c> → <c>IsInitOnly = true</c></item>
///   <item><c>{ get; set; }</c> → neither flag</item>
/// </list>
/// Each emitter then projects:
/// <list type="bullet">
///   <item>TS: <c>readonly</c> modifier for <c>IsReadOnly</c></item>
///   <item>OpenAPI: <c>readOnly: true</c> for <c>IsReadOnly</c></item>
///   <item>Python (Pydantic): <c>Field(frozen=True)</c> for <c>IsReadOnly</c></item>
/// </list>
/// Init-only is a .NET-only concept (no cross-target projection); it matters
/// only to the Dto generator, tested separately there.
/// </para>
/// </summary>
public class ComputedPropertiesTests
{
    private const string Src = """
        using ZibStack.NET.TypeGen;
        namespace Comp;

        [GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi | TypeTarget.Python, OutputDir = ".")]
        public class Order
        {
            public int Id { get; set; }
            public int Quantity { get; set; }
            public decimal UnitPrice { get; set; }

            // Computed — no setter, pure expression.
            public decimal Total => Quantity * UnitPrice;

            // Server-assigned — no public setter.
            public System.DateTime CreatedAt { get; private set; }

            // Set at construction only.
            public string Sku { get; init; } = "";
        }
        """;

    [Fact]
    public void SetterShape_DrivesSchemaFlags()
    {
        var model = BuildModel();
        var order = model.Classes.Single(c => c.SourceName == "Order");

        Prop(order, "Id", expectReadOnly: false, expectInitOnly: false);
        Prop(order, "Total", expectReadOnly: true, expectInitOnly: false);
        Prop(order, "CreatedAt", expectReadOnly: true, expectInitOnly: false);
        Prop(order, "Sku", expectReadOnly: false, expectInitOnly: true);
    }

    [Fact]
    public void TypeScript_EmitsReadonly_OnlyForReadOnlyProps()
    {
        var model = BuildModel();
        var ts = TypeScriptEmitter.Emit(model, new GlobalSettings()).Single(f => f.FileName == "Order.ts").Content;

        // Read-only accessors → `readonly` modifier AND optional (`?`): blocks
        // client reassignment, but doesn't force the field at construction time
        // when a client is building a request payload.
        Assert.Contains("readonly total?: string;", ts);
        Assert.Contains("readonly createdAt?: string;", ts);

        // Init-only stays mutable in TS (no equivalent modifier; Dto pipeline
        // enforces the create-time-only shape server-side).
        Assert.Contains("sku: string;", ts);
        Assert.DoesNotContain("readonly sku", ts);

        // Plain accessors unchanged.
        Assert.Contains("id: number;", ts);
        Assert.DoesNotContain("readonly id", ts);
    }

    [Fact]
    public void OpenApi_EmitsReadOnlyTrue_OnlyForReadOnlyProps()
    {
        var model = BuildModel();
        var yaml = OpenApiEmitter.Emit(model, new GlobalSettings()).Single().Content;

        // Exactly two `readOnly: true` lines — one for Total, one for CreatedAt.
        var roLines = System.Text.RegularExpressions.Regex.Matches(yaml, @"\n\s+readOnly: true\b").Count;
        Assert.Equal(2, roLines);

        // Per-property check (10-space indent is the properties-block depth).
        Assert.Matches(@"Total:\r?\n(?:          [^\n]*\r?\n)*?          readOnly: true", yaml);
        Assert.Matches(@"CreatedAt:\r?\n(?:          [^\n]*\r?\n)*?          readOnly: true", yaml);

        // Read-only props must NOT appear in the schema's `required` list:
        // conventional codegen uses `readOnly + required` to mean "required in
        // response, omitted in request", but we emit one schema for both paths,
        // so keeping them required would break client-side request construction.
        // Match the `required:` block that directly follows `Order:` anywhere
        // in the doc — non-greedy so it doesn't spill into the next schema.
        var reqMatch = System.Text.RegularExpressions.Regex.Match(
            yaml,
            @"Order:\r?\n[\s\S]*?required:\r?\n((?:\s+- [^\r\n]+\r?\n)+)");
        Assert.True(reqMatch.Success, "Order schema has no required block in yaml.");
        var reqList = reqMatch.Groups[1].Value;
        Assert.DoesNotContain("- Total", reqList);
        Assert.DoesNotContain("- CreatedAt", reqList);
        // Plain scalar props still required.
        Assert.Contains("- Id", reqList);
    }

    [Fact]
    public void Python_EmitsFrozenField_OnlyForReadOnlyProps()
    {
        var model = BuildModel();
        var py = PythonEmitter.Emit(model, new GlobalSettings()).Single(f => f.FileName == "order.py").Content;

        // Read-only props become Optional (`T | None`) with `default=None` + frozen:
        // the single-model shape serves both read and write, so client-side
        // construction doesn't need to provide a value. `frozen=True` still
        // blocks reassignment on values parsed from server responses.
        Assert.Contains("total: str | None = Field(default=None, alias=\"Total\", frozen=True)", py);
        Assert.Contains("created_at: datetime | None = Field(default=None, alias=\"CreatedAt\", frozen=True)", py);

        // Init-only maps to a normal Pydantic field — the "write-once" semantic
        // doesn't carry over to Python, and faking it with frozen=True would
        // block legitimate create-time assignments.
        Assert.Contains("sku: str = Field(alias=\"Sku\")", py);
        Assert.DoesNotMatch(@"sku:.*frozen=True", py);

        // Sanity: ordinary rw field doesn't get frozen either.
        Assert.DoesNotMatch(@"id:.*frozen=True", py);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static void Prop(SchemaClass cls, string name, bool expectReadOnly, bool expectInitOnly)
    {
        var p = cls.Properties.Single(x => x.SourceName == name);
        Assert.Equal(expectReadOnly, p.IsReadOnly);
        Assert.Equal(expectInitOnly, p.IsInitOnly);
    }

    private static SchemaModel BuildModel()
    {
        const string stubs = """
            namespace ZibStack.NET.TypeGen
            {
                [System.Flags] public enum TypeTarget { None = 0, TypeScript = 1, OpenApi = 2, Python = 4 }
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct | System.AttributeTargets.Enum)]
                public sealed class GenerateTypesAttribute : System.Attribute {
                    public TypeTarget Targets { get; set; }
                    public string? OutputDir { get; set; }
                }
            }
            """;
        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.DateTime).Assembly.Location),
        };
        var compilation = CSharpCompilation.Create(
            "ComputedTest",
            new[] { CSharpSyntaxTree.ParseText(stubs), CSharpSyntaxTree.ParseText(Src) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var sym = compilation.GetTypeByMetadataName("Comp.Order")!;
        var model = new SchemaModel();
        model.Classes.Add(SchemaParser.ParseClass(sym)!);
        return model;
    }
}
