using System.Linq;
using Microsoft.OpenApi.Readers;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// Integration tests that round-trip the emitter's YAML / JSON output through the
/// official <c>Microsoft.OpenApi.Readers</c> parser. Unit tests asserting on
/// substrings are brittle — they miss structural issues (malformed YAML, duplicate
/// keys, invalid $ref targets) that a real parser catches. Anything that parses
/// cleanly via <c>OpenApiStreamReader</c> and reports zero diagnostics is a
/// well-formed OpenAPI document.
/// </summary>
public class OpenApiValidationTests
{
    private static SchemaClass Cls(string name, params (string Name, string CSharpType, bool Nullable)[] props)
    {
        var c = new SchemaClass
        {
            CSharpFullName = name, SourceName = name, EmittedName = name,
            OutputDir = ".", Targets = TypeTarget.OpenApi,
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
    public void CrossReferencedModel_EmitsValidOpenApi()
    {
        var model = new SchemaModel();
        model.Classes.Add(Cls("Order",
            ("Id", "int", false),
            ("Customer", "string", false),
            ("Items", "System.Collections.Generic.List<OrderItem>", false),
            ("Note", "string", true)));
        model.Classes.Add(Cls("OrderItem",
            ("Sku", "string", false),
            ("Quantity", "int", false)));
        var en = new SchemaEnum
        {
            CSharpFullName = "OrderStatus", SourceName = "OrderStatus",
            EmittedName = "OrderStatus", Targets = TypeTarget.OpenApi, OutputDir = ".",
        };
        en.Members.Add(new SchemaEnumMember { Name = "Pending", Value = 0 });
        en.Members.Add(new SchemaEnumMember { Name = "Shipped", Value = 1 });
        model.Enums.Add(en);

        var yaml = OpenApiEmitter.Emit(model, new GlobalSettings()).Single().Content;
        var doc = ParseYaml(yaml, out var diagnostics);

        Assert.NotNull(doc);
        Assert.Empty(diagnostics.Errors);

        Assert.Contains("Order", doc!.Components.Schemas.Keys);
        Assert.Contains("OrderItem", doc.Components.Schemas.Keys);

        // Cross-reference resolved by the OpenAPI reader itself — proves our $ref is legal.
        var orderSchema = doc.Components.Schemas["Order"];
        var itemsProperty = orderSchema.Properties["Items"];
        Assert.Equal("array", itemsProperty.Type);
        Assert.NotNull(itemsProperty.Items);
        Assert.Equal("OrderItem", itemsProperty.Items.Reference?.Id);

        // Nullable primitive in 3.1 uses a type UNION with 'null'. Our emitter writes
        // that as "type: [string, 'null']" which OpenApi.NET surfaces with Nullable=true.
        var note = orderSchema.Properties["Note"];
        Assert.True(note.Nullable || (note.Type is null));
    }

    [Fact]
    public void JsonOutput_AlsoParsesCleanly()
    {
        var settings = new GlobalSettings();
        settings.OpenApi.OutputPath = "openapi.json";
        var model = ModelWith(Cls("Order", ("Id", "int", false), ("Name", "string", false)));

        var json = OpenApiEmitter.Emit(model, settings).Single().Content;
        var doc = ParseJson(json, out var diagnostics);

        Assert.NotNull(doc);
        Assert.Empty(diagnostics.Errors);
        Assert.Contains("Order", doc!.Components.Schemas.Keys);
    }

    [Fact]
    public void RequiredFields_CorrectlyExcludeNullables()
    {
        var cls = Cls("Order",
            ("Id", "int", false),
            ("Optional", "string", true));
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;
        var doc = ParseYaml(yaml, out var _);

        var schema = doc!.Components.Schemas["Order"];
        Assert.Contains("Id", schema.Required);
        Assert.DoesNotContain("Optional", schema.Required);
    }

    [Fact]
    public void EnumSchema_HasExpectedMembers()
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
        var doc = ParseYaml(yaml, out var diagnostics);

        Assert.Empty(diagnostics.Errors);
        var schema = doc!.Components.Schemas["Status"];
        Assert.Equal("string", schema.Type);
        var members = schema.Enum.Select(o =>
            ((Microsoft.OpenApi.Any.OpenApiString)o).Value).ToList();
        Assert.Equal(new[] { "Pending", "Done" }, members);
    }

    private static Microsoft.OpenApi.Models.OpenApiDocument? ParseYaml(
        string yaml, out Microsoft.OpenApi.Readers.OpenApiDiagnostic diagnostics)
    {
        using var ms = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml));
        return new OpenApiStreamReader().Read(ms, out diagnostics);
    }

    private static Microsoft.OpenApi.Models.OpenApiDocument? ParseJson(
        string json, out Microsoft.OpenApi.Readers.OpenApiDiagnostic diagnostics)
    {
        using var ms = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return new OpenApiStreamReader().Read(ms, out diagnostics);
    }
}
