using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ZibStack.NET.TypeGen;

namespace SampleApi.Models;

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi | TypeTarget.Zod | TypeTarget.TanStackQuery,
               OutputDir = "generated")]
public class WorkItemSummary
{
    public Guid Id { get; set; }
    public required string Title { get; set; } = "";
    public WorkItemState State { get; set; }
    public WorkItemPriority Priority { get; set; }
    public string? OwnerDisplayName { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<string> Labels { get; set; } = new();
}

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi | TypeTarget.Zod | TypeTarget.TanStackQuery,
               OutputDir = "generated")]
public class WorkItemDetail : WorkItemSummary
{
    public string DescriptionMarkdown { get; set; } = "";
    public List<WorkItemEvent> Timeline { get; set; } = new();
    public Dictionary<string, string> CustomFields { get; set; } = new();
}

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi | TypeTarget.Zod | TypeTarget.TanStackQuery,
               OutputDir = "generated")]
public class CreateWorkItemCommand
{
    [MinLength(3)]
    [MaxLength(120)]
    public required string Title { get; set; } = "";

    [MinLength(10)]
    public string DescriptionMarkdown { get; set; } = "";

    public WorkItemPriority Priority { get; set; } = WorkItemPriority.Normal;
    public Guid? OwnerId { get; set; }
    public List<string> Labels { get; set; } = new();
    public DateTimeOffset? DueAt { get; set; }
}

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi | TypeTarget.Zod | TypeTarget.TanStackQuery,
               OutputDir = "generated")]
public class BulkTransitionCommand
{
    public List<Guid> ItemIds { get; set; } = new();
    public WorkItemState TargetState { get; set; }
    public string? Reason { get; set; }
}

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi | TypeTarget.Zod | TypeTarget.TanStackQuery,
               OutputDir = "generated")]
public class AddWorkItemCommentCommand
{
    [MinLength(1)]
    public required string Markdown { get; set; } = "";

    public List<string> MentionedUserIds { get; set; } = new();
}

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi | TypeTarget.Zod | TypeTarget.TanStackQuery,
               OutputDir = "generated")]
public class TransitionResult
{
    public Guid ItemId { get; set; }
    public WorkItemState PreviousState { get; set; }
    public WorkItemState State { get; set; }
    public bool Applied { get; set; }
    public string? Message { get; set; }
}

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi | TypeTarget.Zod | TypeTarget.TanStackQuery,
               OutputDir = "generated")]
public class WorkItemEvent
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string ActorDisplayName { get; set; } = "";
    public WorkItemEventKind Kind { get; set; }
    public string? Markdown { get; set; }
}

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi | TypeTarget.Zod | TypeTarget.TanStackQuery,
               OutputDir = "generated")]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkItemState
{
    Backlog,
    Ready,
    InProgress,
    Blocked,
    Done,
}

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi | TypeTarget.Zod | TypeTarget.TanStackQuery,
               OutputDir = "generated")]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkItemPriority
{
    Low,
    Normal,
    High,
    Critical,
}

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi | TypeTarget.Zod | TypeTarget.TanStackQuery,
               OutputDir = "generated")]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkItemEventKind
{
    Created,
    Commented,
    Assigned,
    Transitioned,
}
