using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.OpenApi.Readers;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

/// <summary>
/// End-to-end tests for <see cref="EndpointDiscovery.Populate"/> over
/// hand-written <c>[ApiController]</c> classes. Compiles a small C# fragment,
/// runs the scan, then drives the <see cref="OpenApiEmitter"/> and asserts on
/// the emitted <c>paths:</c> block.
/// </summary>
public class NativeControllerScanTests
{
    /// <summary>
    /// Compile a C# source string with ASP.NET Core reference assemblies
    /// resolved from <c>typeof(...).Assembly.Location</c>. <c>typeof</c> at
    /// compile-time forces both the static reference and the runtime load,
    /// so the resulting <see cref="Compilation"/> sees Mvc / Routing types.
    /// Minimal BCL refs (object, enumerable) are added alongside.
    /// </summary>
    private static Compilation Compile(string source)
    {
        var refs = new[]
        {
            typeof(object).Assembly,                                    // System.Private.CoreLib
            typeof(System.Linq.Enumerable).Assembly,                    // System.Linq
            typeof(System.ComponentModel.Component).Assembly,           // System.ComponentModel.Primitives
            typeof(System.Collections.Generic.List<>).Assembly,         // System.Collections
            typeof(System.Threading.Tasks.Task).Assembly,               // System.Threading.Tasks
            typeof(Microsoft.AspNetCore.Mvc.ApiControllerAttribute).Assembly,   // Microsoft.AspNetCore.Mvc.Core
            typeof(Microsoft.AspNetCore.Mvc.ControllerBase).Assembly,   // same asm in most versions
            typeof(Microsoft.AspNetCore.Mvc.RouteAttribute).Assembly,   // may alias
            typeof(Microsoft.AspNetCore.Mvc.HttpGetAttribute).Assembly,
            typeof(Microsoft.AspNetCore.Http.IResult).Assembly,         // Microsoft.AspNetCore.Http.Abstractions
            typeof(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder).Assembly, // Microsoft.AspNetCore.Routing
            typeof(Attribute).Assembly,
        }
        .Distinct()
        .Where(a => !string.IsNullOrEmpty(a.Location))
        .Select(a => MetadataReference.CreateFromFile(a.Location))
        .Cast<MetadataReference>()
        // System.Runtime / netstandard facade is needed for Compilation to resolve
        // basic types without noise — add every netstandard-ish reference assembly
        // we can pull off the currently-loaded asms as a belt-and-suspenders step.
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

        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create(
            "test",
            new[] { syntaxTree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    [Fact]
    public void SimpleController_EmitsGetEndpoint()
    {
        var src = """
            using Microsoft.AspNetCore.Mvc;
            namespace Test;

            [ApiController]
            [Route("api/orders")]
            public class OrdersController : ControllerBase
            {
                [HttpGet("{id}")]
                public ActionResult<OrderResponse> GetOrderById(int id) => throw null!;
            }
            public class OrderResponse { public int Id { get; set; } }
            """;
        var compilation = Compile(src);
        // Sanity: make sure the test fragment actually compiled against real
        // Mvc types — otherwise the scanner short-circuits and the asserts below
        // would silently pass on an empty endpoint list.
        Assert.NotNull(compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ControllerBase"));

        var model = new SchemaModel();
        EndpointDiscovery.Populate(model, compilation);

        var ep = Assert.Single(model.Endpoints);
        Assert.Equal("get", ep.Verb);
        Assert.Equal("/api/orders/{id}", ep.Pattern);
        Assert.Equal("getOrderById", ep.OperationId);
        Assert.Equal("Orders", ep.Tag);
        Assert.Equal(EndpointSource.Controller, ep.Source);
        var p = Assert.Single(ep.Parameters);
        Assert.Equal("id", p.Name);
        Assert.Equal(ParamLocation.Route, p.Location);
    }

    [Fact]
    public void ControllerWithAllVerbs_EmitsFourEndpoints()
    {
        var src = """
            using Microsoft.AspNetCore.Mvc;
            namespace Test;

            [ApiController]
            [Route("api/[controller]")]
            public class OrdersController : ControllerBase
            {
                [HttpGet] public ActionResult<OrderResponse> List() => throw null!;
                [HttpPost] public ActionResult<OrderResponse> Create([FromBody] CreateOrderRequest req) => throw null!;
                [HttpPatch("{id}")] public ActionResult<OrderResponse> Update(int id, [FromBody] UpdateOrderRequest req) => throw null!;
                [HttpDelete("{id}")] public IActionResult Delete(int id) => throw null!;
            }
            public class OrderResponse { public int Id { get; set; } }
            public class CreateOrderRequest { public string Name { get; set; } = ""; }
            public class UpdateOrderRequest { public string? Name { get; set; } }
            """;
        var compilation = Compile(src);
        Assert.NotNull(compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ControllerBase"));

        var model = new SchemaModel();
        EndpointDiscovery.Populate(model, compilation);

        Assert.Equal(4, model.Endpoints.Count);
        Assert.Contains(model.Endpoints, e => e.Verb == "get" && e.Pattern == "/api/Orders");
        Assert.Contains(model.Endpoints, e => e.Verb == "post" && e.Pattern == "/api/Orders");
        Assert.Contains(model.Endpoints, e => e.Verb == "patch" && e.Pattern == "/api/Orders/{id}");
        Assert.Contains(model.Endpoints, e => e.Verb == "delete" && e.Pattern == "/api/Orders/{id}");

        // [FromBody] param was bound as request body, NOT as a regular parameter.
        // RequestBodyCSharpType is the full FQN ("Test.CreateOrderRequest"); the
        // emitter strips the namespace when producing the $ref.
        var create = model.Endpoints.Single(e => e.Verb == "post");
        Assert.Contains("CreateOrderRequest", create.RequestBodyCSharpType);
        Assert.DoesNotContain(create.Parameters, p => p.Name == "req");

        // Emit to YAML and verify the $ref drops the namespace.
        var yaml = OpenApiEmitter.Emit(model, new GlobalSettings()).Single().Content;
        Assert.Contains("#/components/schemas/CreateOrderRequest", yaml);
        Assert.DoesNotContain("#/components/schemas/Test.CreateOrderRequest", yaml);
    }

    [Fact]
    public void NativeScan_OverridesCrudApiSynth_OnCollision()
    {
        // Both sources claim GET /api/orders/{id}: native controller method wins.
        var src = """
            using Microsoft.AspNetCore.Mvc;
            namespace Test;

            [ApiController]
            [Route("api/orders")]
            public class OrdersController : ControllerBase
            {
                [HttpGet("{id}")]
                public ActionResult<string> CustomGet(int id) => throw null!;
            }
            """;
        var compilation = Compile(src);
        Assert.NotNull(compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ControllerBase"));

        var model = new SchemaModel();
        // Simulate a [CrudApi] class on "Order" that would synth GET /api/orders/{id}.
        model.Classes.Add(new SchemaClass
        {
            CSharpFullName = "Order", SourceName = "Order", EmittedName = "Order",
            OutputDir = ".", Targets = TypeTarget.OpenApi,
            Crud = new CrudApiInfo { Operations = CrudOperations.GetById },
        });
        model.Classes[0].Properties.Add(new SchemaProperty { SourceName = "Id", CSharpTypeFullName = "int" });

        EndpointDiscovery.Populate(model, compilation);

        // One (verb,pattern) pair survives: native.
        var ep = model.Endpoints.Single(e => e.Verb == "get" && e.Pattern == "/api/orders/{id}");
        Assert.Equal(EndpointSource.Controller, ep.Source);
        Assert.Equal("customGet", ep.OperationId);  // from method name, not crud convention
    }

    [Fact]
    public void ControllerEndpointShowsUpInEmittedYaml()
    {
        var src = """
            using Microsoft.AspNetCore.Mvc;
            namespace Test;

            [ApiController]
            [Route("api/widgets")]
            public class WidgetsController : ControllerBase
            {
                [HttpGet("{id}")]
                public ActionResult<WidgetResponse> Get(int id) => throw null!;
            }
            public class WidgetResponse { public int Id { get; set; } }
            """;
        var compilation = Compile(src);
        Assert.NotNull(compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ControllerBase"));

        var model = new SchemaModel();
        EndpointDiscovery.Populate(model, compilation);

        var yaml = OpenApiEmitter.Emit(model, new GlobalSettings()).Single().Content;

        Assert.Contains("/api/widgets/{id}:", yaml);
        Assert.Contains("    get:", yaml);
        Assert.Contains("operationId: get", yaml);
        Assert.Contains("tags: [Widgets]", yaml);
        // Parameter bound from route.
        Assert.Contains("- name: id", yaml);
        Assert.Contains("in: path", yaml);

        // YAML round-trips through the Microsoft.OpenApi reader — catches any
        // structural error (stray indent, missing required field, etc.).
        var doc = new OpenApiStreamReader().Read(
            new MemoryStream(Encoding.UTF8.GetBytes(yaml)), out _);
        Assert.NotNull(doc);
    }
}
