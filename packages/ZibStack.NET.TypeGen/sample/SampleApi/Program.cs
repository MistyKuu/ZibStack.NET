// Sample for ZibStack.NET.TypeGen — exercises all three endpoint-discovery paths:
//
//   1. [CrudApi] synthesis (Models/Order.cs)      → /api/orders CRUD ops
//   2. Hand-written Minimal API (this file)       → /api/health, /api/echo
//   3. Hand-written [ApiController] (this file)   → /api/widgets/{id}
//
// After `dotnet build`, check ./generated/openapi.yaml for the emitted `paths:`
// block — all three sources contribute without any per-source wiring. The
// generator runs inside the compiler, no running app needed for emission.
//
// Run `dotnet run` to start the server and exercise the endpoints live.

using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();

// ── Hand-written Minimal API ─────────────────────────────────────────────────
// TypeGen scans these via syntax — literal path, inline lambda, parameter
// binding by [FromX] or convention. Shows up in openapi.yaml next to CrudApi
// synthesized paths.

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/echo", ([FromBody] EchoRequest req) => new EchoResponse { Message = req.Text });

var adminGroup = app.MapGroup("/api/admin");
adminGroup.MapGet("/ping", () => "pong");

app.Run();

// DTOs used by the Minimal API endpoints above. [GenerateTypes] pulls them into
// the emitted TS / OpenAPI / Python / Zod so the client gets matching types.
[ZibStack.NET.TypeGen.GenerateTypes(
    Targets = ZibStack.NET.TypeGen.TypeTarget.TypeScript
            | ZibStack.NET.TypeGen.TypeTarget.OpenApi
            | ZibStack.NET.TypeGen.TypeTarget.Zod,
    OutputDir = "generated")]
public class EchoRequest
{
    public string Text { get; set; } = "";
}

[ZibStack.NET.TypeGen.GenerateTypes(
    Targets = ZibStack.NET.TypeGen.TypeTarget.TypeScript
            | ZibStack.NET.TypeGen.TypeTarget.OpenApi
            | ZibStack.NET.TypeGen.TypeTarget.Zod,
    OutputDir = "generated")]
public class EchoResponse
{
    public string Message { get; set; } = "";
}

// ── Hand-written [ApiController] ─────────────────────────────────────────────
// TypeGen scans these via symbol attributes — route template + method [HttpX]
// attributes + [FromRoute]/[FromBody]/etc. Same output shape as Minimal API.

[ApiController]
[Route("api/widgets")]
public class WidgetsController : ControllerBase
{
    [HttpGet("{id}")]
    public ActionResult<WidgetResponse> Get(int id) =>
        new WidgetResponse { Id = id, Name = "widget-" + id };
}

[ZibStack.NET.TypeGen.GenerateTypes(
    Targets = ZibStack.NET.TypeGen.TypeTarget.TypeScript
            | ZibStack.NET.TypeGen.TypeTarget.OpenApi
            | ZibStack.NET.TypeGen.TypeTarget.Zod,
    OutputDir = "generated")]
public class WidgetResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
