using ZibStack.NET.Core;
using ZibStack.NET.Dto;
using ZibStack.NET.TypeGen;
using ZibStack.NET.UI;
using ZibStack.NET.Validation;

namespace ZibStack.NET.Dto.Sample.Models;

[CrudApi]
[UiForm]
[UiTable(DefaultSort = "Name")]
[ZValidate]
// TypeGen discovers [CrudApi] via metadata name and contributes the matching
// paths: block to generated/openapi.yaml (GET/POST/PATCH/DELETE). The TS
// interface is emitted alongside so a frontend can type-check against the same
// contract without a second tool.
[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi,
               OutputDir = "generated")]
public partial class Team : IValidationConfigurator<Team>
{
    public void Configure(IValidationBuilder<Team> b)
    {
        // Collection validation — check count via cross-field rule
        b.Rule(x => x.Players.Count <= x.MaxMembers, "Team has more players than MaxMembers allows");

        // Null-safe nested access — use ?. to avoid NRE when Description is null
        b.Rule(x => x.Description == null || x.Description.Length >= 10,
            "Description must be at least 10 characters if provided");
    }

    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    [UiFormIgnore]
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [ZRequired] [ZMaxLength(100)]
    [UiFormField(Label = "Team Name")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [ZMaxLength(500)]
    [UiFormField(Label = "Description")]
    [TextArea(Rows = 3)]
    [UiTableColumn(Filterable = false)]
    public string? Description { get; set; }

    [ZRange(1, 100)]
    [UiFormField(Label = "Max Members")]
    [UiTableColumn(Sortable = true)]
    public int MaxMembers { get; set; }

    [DtoIgnore] [UiFormIgnore] [UiTableIgnore]
    [OneToMany(Label = "Players")]
    public ICollection<Player> Players { get; set; } = new List<Player>();

    // Audit
    [DtoIgnore] [UiFormIgnore] public DateTime CreatedAt { get; set; }
    [DtoIgnore] [UiFormIgnore] public DateTime UpdatedAt { get; set; }
}
