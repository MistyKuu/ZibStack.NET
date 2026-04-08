using ZibStack.NET.Dto;
using ZibStack.NET.UI;
using ZibStack.NET.Validation;

namespace ZibStack.NET.Dto.Sample.Models;

[CrudApi]
[Form]
[Table(DefaultSort = "Name")]
[Validate]
public partial class Team
{
    [DtoIgnore]
    [FormIgnore]
    [TableColumn(IsVisible = false)]
    public int Id { get; set; }

    [Required] [MaxLength(100)]
    [FormField(Label = "Team Name")]
    [TableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [MaxLength(500)]
    [FormField(Label = "Description")]
    [TextArea(Rows = 3)]
    [TableColumn(Filterable = false)]
    public string? Description { get; set; }

    [Range(1, 100)]
    [FormField(Label = "Max Members")]
    [TableColumn(Sortable = true)]
    public int MaxMembers { get; set; }

    // Audit
    [DtoIgnore] [FormIgnore] public DateTime CreatedAt { get; set; }
    [DtoIgnore] [FormIgnore] public DateTime UpdatedAt { get; set; }
}
