using System.Linq;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// Unit tests for the OpenAPI emitter. Parses the emitted YAML / JSON with string
/// assertions rather than a full YAML parser — the output is small and the test
/// surface is narrow (specific field presence), so brittle-to-exact-layout
/// assertions are fine and easier to debug when they fail.
/// </summary>
public class OpenApiEmitterTests
{
    private static SchemaClass Cls(string name,
        TypeTarget targets = TypeTarget.OpenApi,
        params (string Name, string CSharpType, bool Nullable)[] props)
    {
        var c = new SchemaClass
        {
            CSharpFullName = name, SourceName = name, EmittedName = name,
            OutputDir = ".", Targets = targets,
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

    [Fact]
    public void EmitsOpenApi30ByDefault()
    {
        // Pinned to 3.0.3 because Microsoft.OpenApi.Readers 1.6.x (used by our
        // validation tests) doesn't support 3.1 yet. Revisit when that lib updates.
        var yaml = OpenApiEmitter.Emit(ModelWith(Cls("Order")), new GlobalSettings()).Single().Content;
        Assert.Contains("openapi: 3.0.3", yaml);
    }

    [Fact]
    public void InfoTitleAndVersion_ComeFromSettings()
    {
        var settings = new GlobalSettings();
        settings.OpenApi.Title = "Order Service";
        settings.OpenApi.Version = "2.1.0";
        var yaml = OpenApiEmitter.Emit(ModelWith(Cls("Order")), settings).Single().Content;
        Assert.Contains("title: Order Service", yaml);
        Assert.Contains("version: 2.1.0", yaml);
    }

    [Fact]
    public void PrimitiveTypes_MapToIntegerStringNumberBoolean()
    {
        var cls = Cls("Order",
            props: new[]
            {
                ("Id", "int", false),
                ("Name", "string", false),
                ("Price", "decimal", false),
                ("Active", "bool", false),
            });
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        Assert.Contains("type: integer", yaml);
        Assert.Contains("format: int32", yaml);
        Assert.Contains("type: string", yaml);
        Assert.Contains("type: number", yaml);
        Assert.Contains("type: boolean", yaml);
    }

    [Fact]
    public void GuidAndDateTime_GetFormatHints()
    {
        var cls = Cls("Entity",
            props: new[]
            {
                ("Id", "System.Guid", false),
                ("Created", "System.DateTime", false),
            });
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        Assert.Contains("format: uuid", yaml);
        Assert.Contains("format: date-time", yaml);
    }

    [Fact]
    public void ReferenceToUserDto_EmitsRef()
    {
        var item = Cls("OrderItem");
        var order = Cls("Order", props: new[] { ("Item", "OrderItem", false) });
        var yaml = OpenApiEmitter.Emit(ModelWith(order, item), new GlobalSettings()).Single().Content;
        Assert.Contains("$ref: '#/components/schemas/OrderItem'", yaml);
    }

    [Fact]
    public void ArrayOfUserDto_EmitsItemsWithRef()
    {
        var item = Cls("OrderItem");
        var order = Cls("Order", props: new[] { ("Items", "System.Collections.Generic.List<OrderItem>", false) });
        var yaml = OpenApiEmitter.Emit(ModelWith(order, item), new GlobalSettings()).Single().Content;
        Assert.Contains("type: array", yaml);
        Assert.Contains("$ref: '#/components/schemas/OrderItem'", yaml);
    }

    [Fact]
    public void NullableProperty_NotInRequiredList()
    {
        var cls = Cls("Order",
            props: new[]
            {
                ("Id", "int", false),
                ("Note", "string", true),
            });
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        // Required should contain Id but not Note.
        Assert.Contains("- Id", yaml);
        Assert.DoesNotContain("- Note", yaml);
    }

    [Fact]
    public void NullablePrimitive_EmitsNullableTrue()
    {
        var cls = Cls("Order", props: new[] { ("Note", "string", true) });
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        // OpenAPI 3.0 uses `nullable: true` as a sibling of `type`.
        Assert.Contains("type: string", yaml);
        Assert.Contains("nullable: true", yaml);
    }

    [Fact]
    public void OpenApiPropertyMetadata_AddsFormatAndDescription()
    {
        var cls = Cls("Order", props: new[] { ("Callback", "string", false) });
        cls.Properties[0].OpenApiFormat = "uri";
        cls.Properties[0].OpenApiDescription = "Webhook URL.";
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        Assert.Contains("format: uri", yaml);
        Assert.Contains("description: Webhook URL.", yaml);
    }

    [Fact]
    public void OpenApiIgnore_OnProperty_SkipsIt()
    {
        var cls = Cls("Order",
            props: new[]
            {
                ("Id", "int", false),
                ("Secret", "string", false),
            });
        cls.Properties[1].OpenApiIgnore = true;
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        Assert.Contains("Id:", yaml);
        Assert.DoesNotContain("Secret:", yaml);
    }

    [Fact]
    public void OpenApiTypeOverride_ReplacesInferredPrimaryType()
    {
        // decimal is normally `number` — override forces `string` (preserves precision).
        var cls = Cls("Order", props: new[] { ("Total", "decimal", false) });
        cls.Properties[0].OpenApiTypeOverride = "string";
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        Assert.Contains("Total:", yaml);
        Assert.Contains("type: string", yaml);
        Assert.DoesNotContain("type: number", yaml);
    }

    [Fact]
    public void OpenApiRefOverride_EmitsRefInsteadOfInline()
    {
        // No matching schema in the model — the user vouches that AuditTrailV2 exists
        // (hand-written elsewhere). Emitter trusts the override and emits the $ref.
        var cls = Cls("Order", props: new[] { ("Audit", "object", false) });
        cls.Properties[0].OpenApiRefOverride = "AuditTrailV2";
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        Assert.Contains("$ref: '#/components/schemas/AuditTrailV2'", yaml);
        // The Audit property must not also have an inline `type: object` — the $ref replaces it.
        // (Order itself is `type: object` at the schema level — that's expected.)
        var auditSection = yaml.Substring(yaml.IndexOf("Audit:", System.StringComparison.Ordinal));
        Assert.DoesNotContain("type:", auditSection.Substring(0, auditSection.IndexOf('\n', auditSection.IndexOf('\n') + 1)));
    }

    [Fact]
    public void OpenApiSchemaName_RenamesClassInComponents()
    {
        var cls = Cls("Order", props: new[] { ("Id", "int", false) });
        cls.OpenApiNameOverride = "OrderV1";
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        Assert.Contains("    OrderV1:", yaml);
        // Source class name should NOT be the schema key.
        Assert.DoesNotContain("\n    Order:\n", yaml);
    }

    [Fact]
    public void OutputPathJsonExtension_EmitsJsonInstead()
    {
        var settings = new GlobalSettings();
        settings.OpenApi.OutputPath = "openapi.json";
        var cls = Cls("Order", props: new[] { ("Id", "int", false) });
        var file = OpenApiEmitter.Emit(ModelWith(cls), settings).Single();
        Assert.Equal("openapi.json", file.FileName);
        Assert.StartsWith("{", file.Content.TrimStart());
        Assert.Contains("\"openapi\": \"3.0.3\"", file.Content);
        Assert.Contains("\"Order\":", file.Content);
    }

    [Fact]
    public void Enum_EmitsStringEnumSchema()
    {
        var model = new SchemaModel();
        var en = new SchemaEnum
        {
            CSharpFullName = "Status", SourceName = "Status", EmittedName = "Status",
            Targets = TypeTarget.OpenApi, OutputDir = ".",
        };
        en.Members.Add(new SchemaEnumMember { Name = "Pending", Value = 0 });
        en.Members.Add(new SchemaEnumMember { Name = "Done", Value = 1 });
        model.Enums.Add(en);

        var yaml = OpenApiEmitter.Emit(model, new GlobalSettings()).Single().Content;
        Assert.Contains("    Status:", yaml);
        Assert.Contains("type: string", yaml);
        Assert.Contains("- Pending", yaml);
        Assert.Contains("- Done", yaml);
    }
}
