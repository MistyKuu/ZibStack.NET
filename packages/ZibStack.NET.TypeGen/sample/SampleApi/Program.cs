// Sample for ZibStack.NET.TypeGen — exercises all three endpoint-discovery paths:
//
//   1. [CrudApi] synthesis (Models/Order.cs)      → /api/orders CRUD ops
//   2. Hand-written Minimal API (this file)       → /api/health, /api/echo
//   3. Hand-written [ApiController] (this file)   → /api/widgets/{id}
//
// After `dotnet build`, check ./generated/openapi.yaml for the emitted `paths:`
// block and ./generated/api.gen.ts for the TanStack Query client. All three
// sources contribute without any per-source wiring. The generator runs inside
// the compiler, no running app needed for emission.
//
// Run `dotnet run` to start the server and exercise the endpoints live.

using Microsoft.AspNetCore.Mvc;
using SampleApi.Models;
using ZibStack.NET.Dto;

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

var workflowItems = new List<WorkItemSummary>
{
    new()
    {
        Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Title = "Reconcile vendor invoices",
        State = WorkItemState.InProgress,
        Priority = WorkItemPriority.High,
        OwnerDisplayName = "Avery Stone",
        UpdatedAt = DateTimeOffset.UtcNow.AddHours(-3),
        Labels = ["finance", "q2"],
    },
    new()
    {
        Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        Title = "Publish onboarding checklist",
        State = WorkItemState.Ready,
        Priority = WorkItemPriority.Normal,
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        Labels = ["people", "docs"],
    },
};

var workflow = app.MapGroup("/api/workflow").WithTags("Workflow");

workflow.MapGet("/workspaces/{workspaceId:guid}/items",
        (Guid workspaceId,
         [FromQuery] string? search,
         [FromQuery] WorkItemState? state,
         [FromQuery] string[]? labels,
         [FromQuery] int page = 1,
         [FromQuery] int pageSize = 20) =>
        {
            var filtered = workflowItems
                .Where(item => search is null || item.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
                .Where(item => state is null || item.State == state)
                .Where(item => labels is null || labels.Length == 0 || labels.All(item.Labels.Contains))
                .ToList();

            return PaginatedResponse<WorkItemSummary>.Create(filtered, filtered.Count, page, pageSize);
        })
    .WithName("searchWorkItems")
    .WithTags("Workflow");

workflow.MapGet("/workspaces/{workspaceId:guid}/items/{itemId:guid}",
        (Guid workspaceId, Guid itemId, [FromQuery] bool includeTimeline = true) =>
            new WorkItemDetail
            {
                Id = itemId,
                Title = "Reconcile vendor invoices",
                State = WorkItemState.InProgress,
                Priority = WorkItemPriority.High,
                OwnerDisplayName = "Avery Stone",
                UpdatedAt = DateTimeOffset.UtcNow.AddHours(-3),
                Labels = ["finance", "q2"],
                DescriptionMarkdown = "Match invoice lines against approved purchase orders.",
                Timeline = includeTimeline
                    ? [new WorkItemEvent
                    {
                        Id = Guid.NewGuid(),
                        At = DateTimeOffset.UtcNow.AddHours(-2),
                        ActorDisplayName = "Avery Stone",
                        Kind = WorkItemEventKind.Commented,
                        Markdown = "Waiting on updated tax code from AP.",
                    }]
                    : [],
                CustomFields = new Dictionary<string, string>
                {
                    ["department"] = "finance",
                    ["risk"] = "medium",
                },
            })
    .WithName("getWorkItem")
    .WithTags("Workflow");

workflow.MapPost("/workspaces/{workspaceId:guid}/items",
        (Guid workspaceId, [FromBody] CreateWorkItemCommand command) =>
            new WorkItemDetail
            {
                Id = Guid.NewGuid(),
                Title = command.Title,
                State = WorkItemState.Backlog,
                Priority = command.Priority,
                UpdatedAt = DateTimeOffset.UtcNow,
                Labels = command.Labels,
                DescriptionMarkdown = command.DescriptionMarkdown,
            })
    .WithName("createWorkItem")
    .WithTags("Workflow");

workflow.MapPost("/workspaces/{workspaceId:guid}/items:transition",
        (Guid workspaceId, [FromBody] BulkTransitionCommand command) =>
            command.ItemIds.Select(id => new TransitionResult
            {
                ItemId = id,
                PreviousState = WorkItemState.InProgress,
                State = command.TargetState,
                Applied = true,
                Message = command.Reason,
            }).ToList())
    .WithName("bulkTransitionWorkItems")
    .WithTags("Workflow");

workflow.MapPost("/workspaces/{workspaceId:guid}/items/{itemId:guid}/comments",
        (Guid workspaceId, Guid itemId, [FromBody] AddWorkItemCommentCommand command) =>
            new WorkItemEvent
            {
                Id = Guid.NewGuid(),
                At = DateTimeOffset.UtcNow,
                ActorDisplayName = "Avery Stone",
                Kind = WorkItemEventKind.Commented,
                Markdown = command.Markdown,
            })
    .WithName("addWorkItemComment")
    .WithTags("Workflow");

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
