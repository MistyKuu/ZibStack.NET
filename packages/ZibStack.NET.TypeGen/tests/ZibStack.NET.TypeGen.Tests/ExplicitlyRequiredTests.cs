using System.Linq;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// Exercises <see cref="SchemaProperty.IsExplicitlyRequired"/> — set by
/// <c>[Required]</c> / <c>[ZRequired]</c> runtime-validator attributes or the
/// C# 11 <c>required</c> keyword. Overrides NRT nullability across every
/// target: OpenAPI <c>required:</c> list, TypeScript optional <c>?</c>,
/// Zod <c>.nullish()</c>, Pydantic <c>| None = None</c>.
/// </summary>
public class ExplicitlyRequiredTests
{
    private static SchemaClass Cls(string name, TypeTarget targets, params SchemaProperty[] props)
    {
        var c = new SchemaClass
        {
            CSharpFullName = name, SourceName = name, EmittedName = name,
            OutputDir = ".", Targets = targets,
        };
        c.Properties.AddRange(props);
        return c;
    }

    private static SchemaModel ModelWith(params SchemaClass[] classes)
    {
        var m = new SchemaModel();
        m.Classes.AddRange(classes);
        return m;
    }

    // ── OpenAPI: required list picks up [Required] over NRT ─────────────────

    [Fact]
    public void OpenApi_NullableNrtWithRequired_LandsInRequiredList()
    {
        var cls = Cls("Order", TypeTarget.OpenApi,
            new SchemaProperty
            {
                SourceName = "Name",
                CSharpTypeFullName = "string",
                IsNullable = true,   // NRT says `string?`
                IsExplicitlyRequired = true,  // [Required] / [ZRequired] / required
            });
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("required:", yaml);
        Assert.Contains("- Name", yaml);
        // Still nullable on the wire? No — explicit required overrides NRT.
        Assert.DoesNotContain("Name: { type: string, nullable: true }", yaml);
    }

    [Fact]
    public void OpenApi_WithoutRequired_NullableNrtExcludesFromRequired()
    {
        var cls = Cls("Order", TypeTarget.OpenApi,
            new SchemaProperty
            {
                SourceName = "Note",
                CSharpTypeFullName = "string",
                IsNullable = true,
                IsExplicitlyRequired = false,  // plain NRT
            });
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        // Note shouldn't be listed under required — sole property and it's nullable.
        Assert.DoesNotContain("- Note", yaml);
    }

    // ── TypeScript: skip optional `?` marker when required ──────────────────

    [Fact]
    public void TypeScript_NullableNrtWithRequired_NotOptional()
    {
        var cls = Cls("Order", TypeTarget.TypeScript,
            new SchemaProperty
            {
                SourceName = "Name",
                CSharpTypeFullName = "string",
                IsNullable = true,
                IsExplicitlyRequired = true,
            });
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        // No `?` marker — explicit required overrides NRT nullability.
        Assert.Contains("name: string;", ts);
        Assert.DoesNotContain("name?:", ts);
    }

    [Fact]
    public void TypeScript_NullableNrtWithoutRequired_StaysOptional()
    {
        var cls = Cls("Order", TypeTarget.TypeScript,
            new SchemaProperty
            {
                SourceName = "Note",
                CSharpTypeFullName = "string",
                IsNullable = true,
            });
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("note?:", ts);
    }

    // ── Zod: drop .nullish() when required ──────────────────────────────────

    [Fact]
    public void Zod_NullableNrtWithRequired_NoNullish()
    {
        var cls = Cls("Order", TypeTarget.Zod,
            new SchemaProperty
            {
                SourceName = "Name",
                CSharpTypeFullName = "string",
                IsNullable = true,
                IsExplicitlyRequired = true,
            });
        var zod = ZodEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("name: z.string()", zod);
        Assert.DoesNotContain(".nullish()", zod);
    }

    [Fact]
    public void Zod_NullableNrtWithoutRequired_HasNullish()
    {
        var cls = Cls("Order", TypeTarget.Zod,
            new SchemaProperty
            {
                SourceName = "Note",
                CSharpTypeFullName = "string",
                IsNullable = true,
            });
        var zod = ZodEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("note: z.string().nullish()", zod);
    }

    // ── Python: no `| None` / no `default=None` when required ───────────────

    [Fact]
    public void Python_NullableNrtWithRequired_NotNoneTyped()
    {
        var cls = Cls("Order", TypeTarget.Python,
            new SchemaProperty
            {
                SourceName = "Name",
                CSharpTypeFullName = "string",
                IsNullable = true,
                IsExplicitlyRequired = true,
            });
        var py = PythonEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        // Name is required: `name: str = Field(alias="Name")` — no `| None`,
        // no `default=None`.
        Assert.Contains("name: str", py);
        Assert.DoesNotContain("name: str | None", py);
        Assert.DoesNotContain("default=None", py);
    }

    [Fact]
    public void Python_NullableNrtWithoutRequired_StaysOptional()
    {
        var cls = Cls("Order", TypeTarget.Python,
            new SchemaProperty
            {
                SourceName = "Note",
                CSharpTypeFullName = "string",
                IsNullable = true,
            });
        var py = PythonEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("note: str | None", py);
        Assert.Contains("default=None", py);
    }

    // ── precedence: fluent OpenApiNullableOverride beats IsExplicitlyRequired ─

    [Fact]
    public void OpenApi_FluentNullableOverride_BeatsExplicitRequired()
    {
        // User's fluent config explicitly says "this is nullable on the wire" —
        // wins over the attribute-derived IsExplicitlyRequired flag.
        var cls = Cls("Order", TypeTarget.OpenApi,
            new SchemaProperty
            {
                SourceName = "Name",
                CSharpTypeFullName = "string",
                IsNullable = false,
                IsExplicitlyRequired = true,
                OpenApiNullableOverride = true,   // fluent override
            });
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        // With the override, Name shouldn't be in the required list.
        Assert.DoesNotContain("- Name", yaml);
    }
}
