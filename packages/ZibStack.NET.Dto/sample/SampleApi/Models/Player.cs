using ZibStack.NET.Dto;
using ZibStack.NET.UI;
using ZibStack.NET.Validation;

namespace ZibStack.NET.Dto.Sample.Models;

// One model — generates: CRUD API + DTOs + Response + QueryDto + Form UI + Table UI + EF config + Audit
[CrudApi(Operations = CrudOperations.AllWithBulk)]
[Form]
[Table(DefaultSort = "Name", DefaultPageSize = 20)]
[FormGroup("basic", Label = "Basic Info", Order = 1)]
[FormGroup("contact", Label = "Contact", Order = 2)]
[ColumnPermission("Salary", "finance.read")]
[Validate]
public partial class Player
{
    [DtoIgnore]
    [FormIgnore]
    [TableColumn(IsVisible = false)]
    public int Id { get; set; }

    [Required] [MinLength(2)] [MaxLength(50)]
    [FormField(Label = "Name", Placeholder = "Enter player name...", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [Range(1, 100)]
    [FormField(Label = "Level", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public int Level { get; set; }

    [Email]
    [FormField(Label = "Email", Placeholder = "player@example.com", Group = "contact")]
    [TableColumn(Sortable = true, Filterable = true)]
    public string? Email { get; set; }

    [FormField(Label = "Salary", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = false)]  // not filterable in query
    public decimal Salary { get; set; }

    [ListIgnore]  // only in GET /api/players/{id}, not in list
    [FormField(Label = "Bio", Group = "basic")]
    [TextArea(Rows = 5)]
    [TableIgnore]
    public string? Bio { get; set; }

    [CreateOnly]
    [ResponseIgnore]
    [MinLength(8)]
    [FormField(Label = "Password", Group = "contact")]
    [PasswordInput]
    public required string Password { get; set; }

    // Audit fields — auto-filled by generated store
    [DtoIgnore] [FormIgnore] public DateTime CreatedAt { get; set; }
    [DtoIgnore] [FormIgnore] public DateTime UpdatedAt { get; set; }
}
