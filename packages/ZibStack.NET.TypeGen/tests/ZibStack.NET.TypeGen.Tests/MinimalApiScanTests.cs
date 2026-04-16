using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// End-to-end tests for <see cref="EndpointDiscovery"/>'s Minimal API scan.
/// Compiles a small C# fragment with <c>app.MapGet(...)</c>-style calls, runs
/// <see cref="EndpointDiscovery.Populate"/>, and asserts on the resulting
/// <see cref="SchemaModel.Endpoints"/>. Uses the same force-load + explicit
/// asm ref pattern as <see cref="NativeControllerScanTests"/>.
/// </summary>
public class MinimalApiScanTests
{
    private static Compilation Compile(string source)
    {
        // Force-load ASP.NET Core assemblies so their locations are discoverable
        // — the xunit runner doesn't pull them in otherwise.
        _ = typeof(Microsoft.AspNetCore.Builder.WebApplication);
        _ = typeof(Microsoft.AspNetCore.Http.IResult);
        _ = typeof(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder);
        _ = typeof(Microsoft.AspNetCore.Routing.RouteGroupBuilder);
        _ = typeof(Microsoft.AspNetCore.Http.Results);
        _ = typeof(Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions);
        _ = typeof(Microsoft.AspNetCore.Mvc.FromBodyAttribute);

        var refs = new[]
        {
            typeof(object).Assembly,
            typeof(System.Linq.Enumerable).Assembly,
            typeof(System.Collections.Generic.List<>).Assembly,
            typeof(System.Threading.Tasks.Task).Assembly,
            typeof(System.Threading.CancellationToken).Assembly,
            typeof(Attribute).Assembly,
            typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly,
            typeof(Microsoft.AspNetCore.Http.IResult).Assembly,
            typeof(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder).Assembly,
            typeof(Microsoft.AspNetCore.Routing.RouteGroupBuilder).Assembly,
            typeof(Microsoft.AspNetCore.Http.Results).Assembly,
            typeof(Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions).Assembly,
            typeof(Microsoft.AspNetCore.Mvc.FromBodyAttribute).Assembly,
        }
        .Distinct()
        .Where(a => !string.IsNullOrEmpty(a.Location))
        .Select(a => MetadataReference.CreateFromFile(a.Location))
        .Cast<MetadataReference>()
        .Concat(AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic
                && !string.IsNullOrEmpty(a.Location)
                && (a.GetName().Name?.StartsWith("System.") == true
                    || a.GetName().Name == "netstandard"
                    || a.GetName().Name == "mscorlib"))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>())
        .Distinct()
        .ToList();

        var syntax = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create("test",
            new[] { syntax }, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    [Fact]
    public void SimpleMapGet_EmitsEndpoint()
    {
        var src = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();

            app.MapGet("/orders/{id}", (int id) => "order-" + id);

            app.Run();
            """;
        var comp = Compile(src);
        Assert.NotNull(comp.GetTypeByMetadataName("Microsoft.AspNetCore.Routing.IEndpointRouteBuilder"));

        var model = new SchemaModel();
        EndpointDiscovery.Populate(model, comp);

        var ep = Assert.Single(model.Endpoints);
        Assert.Equal("get", ep.Verb);
        Assert.Equal("/orders/{id}", ep.Pattern);
        Assert.Equal(EndpointSource.MinimalApi, ep.Source);
        Assert.Equal("Orders", ep.Tag);
        // `id` is in the route template → bound as route param.
        var p = Assert.Single(ep.Parameters);
        Assert.Equal("id", p.Name);
        Assert.Equal(ParamLocation.Route, p.Location);
    }

    [Fact]
    public void MapGroup_ChainPrefixesPattern()
    {
        var src = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();

            app.MapGroup("/api").MapGroup("/v1").MapGet("/orders", () => "ok");

            app.Run();
            """;
        var comp = Compile(src);
        var model = new SchemaModel();
        EndpointDiscovery.Populate(model, comp);

        var ep = Assert.Single(model.Endpoints);
        Assert.Equal("/api/v1/orders", ep.Pattern);
    }

    [Fact]
    public void MapGroup_ViaLocalVariable_PrefixResolved()
    {
        var src = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();

            var group = app.MapGroup("/api/widgets");
            group.MapGet("/{id}", (int id) => "widget-" + id);

            app.Run();
            """;
        var comp = Compile(src);
        var model = new SchemaModel();
        EndpointDiscovery.Populate(model, comp);

        var ep = Assert.Single(model.Endpoints);
        Assert.Equal("/api/widgets/{id}", ep.Pattern);
    }

    [Fact]
    public void FromBodyLambdaParam_BoundAsRequestBody()
    {
        var src = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.AspNetCore.Routing;

            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();

            app.MapPost("/orders", ([FromBody] CreateOrderRequest req) => Results.Ok());

            app.Run();

            public class CreateOrderRequest { public string Name { get; set; } = ""; }
            """;
        var comp = Compile(src);
        var model = new SchemaModel();
        EndpointDiscovery.Populate(model, comp);

        var ep = Assert.Single(model.Endpoints);
        Assert.Equal("post", ep.Verb);
        Assert.Contains("CreateOrderRequest", ep.RequestBodyCSharpType);
        Assert.DoesNotContain(ep.Parameters, p => p.Name == "req");
    }

    [Fact]
    public void NonLiteralPattern_Skipped()
    {
        var src = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;

            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();

            var prefix = GetPrefix();
            app.MapGet(prefix + "/orders", () => "ok");

            app.Run();

            static string GetPrefix() => "/api";
            """;
        var comp = Compile(src);
        var model = new SchemaModel();
        EndpointDiscovery.Populate(model, comp);

        // Can't resolve the pattern at compile time → endpoint silently skipped.
        Assert.Empty(model.Endpoints);
    }

    [Fact]
    public void ConstStringPattern_IsResolved()
    {
        var src = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;

            const string OrderRoute = "/api/orders";

            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();

            app.MapGet(OrderRoute, () => "ok");

            app.Run();
            """;
        var comp = Compile(src);
        var model = new SchemaModel();
        EndpointDiscovery.Populate(model, comp);

        var ep = Assert.Single(model.Endpoints);
        Assert.Equal("/api/orders", ep.Pattern);
    }

    [Fact]
    public void MinimalApi_OverridesCrudApiSynth_OnCollision()
    {
        var src = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Routing;

            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();

            // Collides with CrudApi synth for GET /api/orders/{id}.
            app.MapGet("/api/orders/{id}", (int id) => "custom");

            app.Run();
            """;
        var comp = Compile(src);
        var model = new SchemaModel();
        model.Classes.Add(new SchemaClass
        {
            CSharpFullName = "Order", SourceName = "Order", EmittedName = "Order",
            OutputDir = ".", Targets = TypeTarget.OpenApi,
            Crud = new CrudApiInfo { Operations = CrudOperations.GetById },
        });
        model.Classes[0].Properties.Add(new SchemaProperty { SourceName = "Id", CSharpTypeFullName = "int" });

        EndpointDiscovery.Populate(model, comp);

        var ep = model.Endpoints.Single(e => e.Verb == "get" && e.Pattern == "/api/orders/{id}");
        Assert.Equal(EndpointSource.MinimalApi, ep.Source);
    }

    [Fact]
    public void ReturnTypeUnwrappedFromLambda()
    {
        var src = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            using System.Threading.Tasks;

            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();

            app.MapGet("/orders/{id}", (int id) => new OrderResponse { Id = id });
            app.MapGet("/async/{id}", async (int id) => { await Task.Yield(); return new OrderResponse { Id = id }; });

            app.Run();

            public class OrderResponse { public int Id { get; set; } }
            """;
        var comp = Compile(src);
        var model = new SchemaModel();
        EndpointDiscovery.Populate(model, comp);

        Assert.Equal(2, model.Endpoints.Count);
        foreach (var ep in model.Endpoints)
        {
            Assert.Contains("OrderResponse", ep.ResponseCSharpType);
        }
    }

    [Fact]
    public void CancellationTokenParam_Filtered()
    {
        var src = """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Routing;
            using System.Threading;

            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();

            app.MapGet("/orders/{id}", (int id, CancellationToken ct) => "ok");

            app.Run();
            """;
        var comp = Compile(src);
        var model = new SchemaModel();
        EndpointDiscovery.Populate(model, comp);

        var ep = Assert.Single(model.Endpoints);
        // CancellationToken is infrastructure — not a query/route param.
        Assert.DoesNotContain(ep.Parameters, p => p.Name == "ct");
        Assert.Single(ep.Parameters, p => p.Name == "id");
    }
}
