using System.Collections.Generic;
using System.Text.Json.Nodes;
using ZibStack.NET.TypeGen;

namespace SampleApi.Models;

// ── Diamond-shaped type graph stress test ───────────────────────────────────
//
// Demonstrates that transitive discovery deduplicates correctly when the same
// type is reachable via multiple paths (StepDownDto? referenced from both
// Pipeline.Forward[].Down and Pipeline.Rollback[].Down). Also exercises deep
// inheritance (ComponentRef → RuleRef → RuleItem) and multiple array properties
// pointing to different leaves of the same base (InitialComponents has three
// array properties, each a different ComponentRef subclass).
//
// Expected: `dotnet build` produces one .ts interface per type, no duplicates.

public enum ComponentKind { Rule, Widget, Layout }

public abstract record ComponentRef
{
    public abstract ComponentKind Kind { get; }
    public string? ExternalId { get; init; }
}

public record RuleRef : ComponentRef
{
    public override ComponentKind Kind => ComponentKind.Rule;
    public string? Name { get; init; }
}

public record RuleItem : RuleRef
{
    public JsonObject? Payload { get; init; }
    public bool? Active { get; set; }
}

public record WidgetItem : ComponentRef
{
    public required string WidgetType { get; init; }
    public JsonObject? Payload { get; init; }
    public override ComponentKind Kind => ComponentKind.Widget;
    public bool? Active { get; set; }
}

public record LayoutRef : ComponentRef
{
    public override ComponentKind Kind => ComponentKind.Layout;
    public string? EntityType { get; init; }
    public int? ViewId { get; init; }
}

public record LayoutItem : LayoutRef
{
    public JsonObject? Payload { get; init; }
    public bool? Active { get; set; }
}

public record StepUpDto(string Action);
public record StepDownDto(string Action);

public record StepDto(
    string Name,
    StepUpDto Up,
    StepDownDto? Down,
    string? Description);

public record PipelineDto()
{
    public required List<StepDto> Forward { get; init; } = [];
    public required List<StepDto> Rollback { get; init; } = [];
}

public record ReleaseDto(string Tag, PipelineDto Pipeline);
public record DependencyDto(string PackageName, string MinVersion, int Priority = 0);

public record InitialComponentsDto()
{
    public RuleItem[] Rules { get; init; } = [];
    public WidgetItem[] Widgets { get; init; } = [];
    public LayoutItem[] Layouts { get; init; } = [];
}

public record PackageRequestDto(
    string Name,
    string? Description,
    InitialComponentsDto Components,
    List<ReleaseDto> Releases,
    List<DependencyDto>? Dependencies);
