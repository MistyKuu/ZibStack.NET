using System.Linq;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// <c>[JsonExtensionData]</c> on a property turns it into the schema-level
/// <c>additionalProperties</c> (OpenAPI) / index signature (TypeScript) — the
/// property itself doesn't appear in the generated output. These tests drive
/// the emitters directly with hand-built <see cref="SchemaClass"/> values
/// since the parser-side detection lives in <see cref="SchemaParser"/> which
/// would need a full Roslyn compilation harness to exercise; the parser logic
/// is small and tested indirectly via the SampleApi build.
/// </summary>
public class JsonExtensionDataTests
{
    private static SchemaClass Cls(string name, params (string Name, string CSharpType, bool Nullable)[] props)
    {
        var c = new SchemaClass
        {
            CSharpFullName = name, SourceName = name, EmittedName = name,
            OutputDir = ".", Targets = TypeTarget.TypeScript | TypeTarget.OpenApi,
        };
        foreach (var (n, t, nu) in props)
            c.Properties.Add(new SchemaProperty { SourceName = n, CSharpTypeFullName = t, IsNullable = nu });
        return c;
    }

    private static SchemaModel ModelWith(params SchemaClass[] classes)
    {
        var m = new SchemaModel();
        m.Classes.AddRange(classes);
        return m;
    }

    // ── basic permissive (object value type) ───────────────────────────

    [Fact]
    public void OpenApi_PermissiveAdditionalProperties_EmitsTrue()
    {
        var cls = Cls("Order", ("Id", "int", false), ("Customer", "string", false));
        cls.AllowsAdditionalProperties = true;
        cls.AdditionalPropertiesValueCSharpType = null;   // object / JsonElement → permissive

        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("additionalProperties: true", yaml);
        // The [JsonExtensionData] property must NOT appear under properties.
        Assert.DoesNotContain("Extra:", yaml);
    }

    [Fact]
    public void TypeScript_PermissiveAdditionalProperties_EmitsUnknownIndexSignature()
    {
        var cls = Cls("Order", ("Id", "int", false), ("Customer", "string", false));
        cls.AllowsAdditionalProperties = true;
        cls.AdditionalPropertiesValueCSharpType = null;

        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).First(f => f.FileName == "Order.ts").Content;

        Assert.Contains("[key: string]: unknown;", ts);
        Assert.Contains("id: number;", ts);
        Assert.Contains("customer: string;", ts);
    }

    // ── typed value type ───────────────────────────────────────────────

    [Fact]
    public void OpenApi_TypedAdditionalProperties_EmitsValueSchema()
    {
        var cls = Cls("Settings", ("Name", "string", false));
        cls.AllowsAdditionalProperties = true;
        cls.AdditionalPropertiesValueCSharpType = "int";

        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("additionalProperties:", yaml);
        Assert.Contains("type: integer", yaml);
        Assert.Contains("format: int32", yaml);
        Assert.DoesNotContain("additionalProperties: true", yaml);
    }

    [Fact]
    public void TypeScript_TypedAdditionalProperties_UnionsValueWithUnknown()
    {
        var cls = Cls("Settings", ("Name", "string", false));
        cls.AllowsAdditionalProperties = true;
        cls.AdditionalPropertiesValueCSharpType = "string";

        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).First(f => f.FileName == "Settings.ts").Content;

        // Union with unknown so named props (which may not be `string`) stay compatible.
        Assert.Contains("[key: string]: string | unknown;", ts);
    }

    [Fact]
    public void OpenApi_AdditionalPropertiesWithUserDtoValue_EmitsRef()
    {
        var tag = Cls("Tag", ("Name", "string", false));
        var settings = Cls("Settings", ("Title", "string", false));
        settings.AllowsAdditionalProperties = true;
        settings.AdditionalPropertiesValueCSharpType = "Tag";

        var yaml = OpenApiEmitter.Emit(ModelWith(settings, tag), new GlobalSettings()).Single().Content;

        Assert.Contains("additionalProperties:", yaml);
        Assert.Contains("$ref: '#/components/schemas/Tag'", yaml);
    }

    // ── inheritance ────────────────────────────────────────────────────

    [Fact]
    public void OpenApi_AdditionalPropertiesOnDerivedClass_LandInsideAllOfBlock()
    {
        // When the base is in the model, child is emitted as allOf — additionalProperties
        // on the child must land inside the `{ type: object, ... }` second item, not on the
        // outer schema (which is just an allOf wrapper).
        var entity = Cls("Entity", ("Id", "int", false));
        var order = Cls("Order", ("Customer", "string", false));
        order.BaseClassFullName = "Entity";
        order.AllowsAdditionalProperties = true;

        var yaml = OpenApiEmitter.Emit(ModelWith(entity, order), new GlobalSettings()).Single().Content;

        var orderStart = yaml.IndexOf("    Order:", System.StringComparison.Ordinal);
        var orderEnd = yaml.IndexOf("    Entity:", System.StringComparison.Ordinal);
        var orderBlock = orderEnd > orderStart ? yaml.Substring(orderStart, orderEnd - orderStart) : yaml.Substring(orderStart);

        Assert.Contains("allOf:", orderBlock);
        Assert.Contains("$ref: '#/components/schemas/Entity'", orderBlock);
        Assert.Contains("additionalProperties: true", orderBlock);
    }

    [Fact]
    public void TypeScript_AdditionalPropertiesOnDerivedInterface_AfterExtends()
    {
        var entity = Cls("Entity", ("Id", "int", false));
        var order = Cls("Order", ("Customer", "string", false));
        order.BaseClassFullName = "Entity";
        order.AllowsAdditionalProperties = true;

        var orderTs = TypeScriptEmitter.Emit(ModelWith(entity, order), new GlobalSettings())
            .First(f => f.FileName == "Order.ts").Content;

        // Index signature must be inside the Order body even though it extends Entity.
        Assert.Contains("export interface Order extends Entity {", orderTs);
        Assert.Contains("[key: string]: unknown;", orderTs);
    }

    // ── interaction with other features ────────────────────────────────

    [Fact]
    public void OpenApi_NoExtensionData_NoAdditionalPropertiesEmitted()
    {
        // Sanity: classes without the flag don't get a stray additionalProperties line.
        var cls = Cls("Plain", ("Id", "int", false));
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        Assert.DoesNotContain("additionalProperties", yaml);
    }

    [Fact]
    public void TypeScript_NoExtensionData_NoIndexSignatureEmitted()
    {
        var cls = Cls("Plain", ("Id", "int", false));
        var ts = TypeScriptEmitter.Emit(ModelWith(cls), new GlobalSettings()).First(f => f.FileName == "Plain.ts").Content;
        Assert.DoesNotContain("[key: string]:", ts);
    }
}
