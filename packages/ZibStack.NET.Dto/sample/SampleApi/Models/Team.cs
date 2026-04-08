using ZibStack.NET.Dto;
using ZibStack.NET.UI;
using ZibStack.NET.Validation;

namespace ZibStack.NET.Dto.Sample.Models;

[CrudApi]
[UiForm]
[UiTable(DefaultSort = "Name")]
[Validate]
public partial class Team
{
    [DtoIgnore]
    [UiFormIgnore]
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [Required] [MaxLength(100)]
    [UiFormField(Label = "Team Name")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [MaxLength(500)]
    [UiFormField(Label = "Description")]
    [TextArea(Rows = 3)]
    [UiTableColumn(Filterable = false)]
    public string? Description { get; set; }

    [Range(1, 100)]
    [UiFormField(Label = "Max Members")]
    [UiTableColumn(Sortable = true)]
    public int MaxMembers { get; set; }

    // Audit
    [DtoIgnore] [UiFormIgnore] public DateTime CreatedAt { get; set; }
    [DtoIgnore] [UiFormIgnore] public DateTime UpdatedAt { get; set; }
}
