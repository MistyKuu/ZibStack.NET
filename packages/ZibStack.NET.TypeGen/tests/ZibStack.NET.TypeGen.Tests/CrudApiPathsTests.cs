using System.Linq;
using Microsoft.OpenApi.Readers;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// Drives <see cref="OpenApiEmitter"/> against <see cref="SchemaClass"/>es that
/// carry <see cref="CrudApiInfo"/>, verifying the emitted <c>paths:</c> block.
/// Each test round-trips the YAML through <c>Microsoft.OpenApi.Readers</c> so
/// structural errors surface as parser diagnostics, not just missing substrings.
/// </summary>
public class CrudApiPathsTests
{
    private static SchemaClass CrudClass(string name, CrudOperations ops = CrudOperations.All, string? route = null, string keyProperty = "Id")
    {
        var cls = new SchemaClass
        {
            CSharpFullName = name,
            SourceName = name,
            EmittedName = name,
            OutputDir = ".",
            Targets = TypeTarget.OpenApi,
            Crud = new CrudApiInfo { Operations = ops, Route = route, KeyProperty = keyProperty },
        };
        cls.Properties.Add(new SchemaProperty { SourceName = keyProperty, CSharpTypeFullName = "int" });
        cls.Properties.Add(new SchemaProperty { SourceName = "Name", CSharpTypeFullName = "string" });
        return cls;
    }

    private static SchemaModel ModelWith(params SchemaClass[] classes)
    {
        var m = new SchemaModel();
        m.Classes.AddRange(classes);
        return m;
    }

    [Fact]
    public void CrudApiAllOps_EmitsFullPathSet()
    {
        var yaml = OpenApiEmitter.Emit(ModelWith(CrudClass("Order")), new GlobalSettings()).Single().Content;

        Assert.Contains("paths:", yaml);
        Assert.Contains("/api/orders:", yaml);
        Assert.Contains("/api/orders/{id}:", yaml);
        Assert.Contains("    get:", yaml);
        Assert.Contains("    post:", yaml);
        Assert.Contains("    patch:", yaml);
        Assert.Contains("    delete:", yaml);

        // DTO naming convention: request = Create{Class}Request / Update{Class}Request, response = {Class}.
        Assert.Contains("#/components/schemas/CreateOrderRequest", yaml);
        Assert.Contains("#/components/schemas/UpdateOrderRequest", yaml);
        Assert.Contains("#/components/schemas/Order", yaml);

        // Same caveat as the JSON test: request DTOs (Create/UpdateOrderRequest)
        // aren't in this stripped-down model. Ignore dangling-ref complaints.
        var doc = new OpenApiStreamReader().Read(
            new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(yaml)), out var diag);
        Assert.DoesNotContain(diag.Errors, e => !e.Message.Contains("Invalid Reference identifier"));
        Assert.Contains("/api/orders", doc!.Paths.Keys);
        Assert.Contains("/api/orders/{id}", doc.Paths.Keys);
    }

    [Fact]
    public void Operations_FlagControlsWhichVerbsEmit()
    {
        // Only GetById + Delete — no POST/PATCH/GET-list expected.
        var yaml = OpenApiEmitter.Emit(ModelWith(
            CrudClass("Team", ops: CrudOperations.GetById | CrudOperations.Delete)),
            new GlobalSettings()).Single().Content;

        Assert.Contains("/api/teams/{id}:", yaml);
        Assert.Contains("    get:", yaml);
        Assert.Contains("    delete:", yaml);
        Assert.DoesNotContain("    post:", yaml);
        Assert.DoesNotContain("    patch:", yaml);
        // No collection route because neither GetList nor Create requested it.
        Assert.DoesNotContain("\n  /api/teams:\n", yaml);
    }

    [Fact]
    public void ExplicitRoute_WinsOverConvention()
    {
        var yaml = OpenApiEmitter.Emit(
            ModelWith(CrudClass("Octopus", route: "v2/sea-creatures")),
            new GlobalSettings()).Single().Content;
        Assert.Contains("/v2/sea-creatures:", yaml);
        Assert.Contains("/v2/sea-creatures/{id}:", yaml);
        // Conventional Octopuses (naive pluralizer) should NOT have appeared.
        Assert.DoesNotContain("octopuss", yaml);
    }

    [Fact]
    public void KeyProperty_CustomName_ReflectsInPathAndParam()
    {
        var cls = CrudClass("Document", keyProperty: "Slug");
        cls.Properties[0] = new SchemaProperty { SourceName = "Slug", CSharpTypeFullName = "string" };
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("/api/documents/{slug}", yaml);
        Assert.Contains("- name: slug", yaml);
        Assert.Contains("type: string", yaml);
    }

    [Fact]
    public void NoCrudClasses_EmitsEmptyPathsObject()
    {
        var plain = new SchemaClass
        {
            CSharpFullName = "Plain", SourceName = "Plain", EmittedName = "Plain",
            OutputDir = ".", Targets = TypeTarget.OpenApi,
        };
        plain.Properties.Add(new SchemaProperty { SourceName = "Id", CSharpTypeFullName = "int" });
        var yaml = OpenApiEmitter.Emit(ModelWith(plain), new GlobalSettings()).Single().Content;

        Assert.Contains("paths: {}", yaml);
        Assert.DoesNotContain("/api/", yaml);
    }

    [Fact]
    public void JsonOutput_AlsoIncludesPaths()
    {
        var settings = new GlobalSettings();
        settings.OpenApi.OutputPath = "openapi.json";
        var json = OpenApiEmitter.Emit(ModelWith(CrudClass("Order")), settings).Single().Content;

        Assert.Contains("\"/api/orders\"", json);
        Assert.Contains("\"/api/orders/{id}\"", json);
        Assert.Contains("\"operationId\": \"createOrder\"", json);

        // Filter out dangling-reference errors — the request DTO schemas
        // (CreateOrderRequest, UpdateOrderRequest) are intentionally out of this
        // minimal model. Structure-level errors would still surface.
        var doc = new OpenApiStreamReader().Read(
            new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)), out var diag);
        Assert.DoesNotContain(diag.Errors, e => !e.Message.Contains("Invalid Reference identifier"));
        Assert.Contains("/api/orders", doc!.Paths.Keys);
    }

    [Fact]
    public void EmittedName_UsedForTagsAndRefs_ButSourceNameForRequestDtos()
    {
        // Class renamed to "OrderV1" via OpenApiNameOverride — requests still key
        // off the C# source name (Create{Order}Request), the response $ref uses
        // the renamed schema key. Mirrors how Dto generator names request DTOs.
        var cls = CrudClass("Order");
        cls.OpenApiNameOverride = "OrderV1";   // emitter resolves EmittedName from this
        var yaml = OpenApiEmitter.Emit(ModelWith(cls), new GlobalSettings()).Single().Content;

        Assert.Contains("tags: [OrderV1]", yaml);
        Assert.Contains("#/components/schemas/OrderV1", yaml);
        Assert.Contains("#/components/schemas/CreateOrderRequest", yaml);  // source name
        Assert.DoesNotContain("CreateOrderV1Request", yaml);
    }
}
