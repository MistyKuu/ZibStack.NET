using ZibStack.NET.Dto;
using ZibStack.NET.UI;
using ZibStack.NET.Validation;

namespace SampleApi.Models;

public enum PlayerRole
{
    Player,
    Moderator,
    Admin
}

// [Model] = [CrudApi] + [Form] + [Table] + [Validate] — all from one attribute!
[Model(DefaultSort = "Name", DefaultPageSize = 20)]
[FormGroup("basic", Label = "Basic Info", Order = 1)]
[FormGroup("contact", Label = "Contact", Order = 2)]
public partial class Player
{
    [FormIgnore]
    [TableColumn(IsVisible = false)]
    public int Id { get; set; }

    [Required] [MinLength(2)] [MaxLength(50)]
    [FormField(Label = "Player Name", Placeholder = "Enter name...", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [Range(1, 100)]
    [Slider(Min = 1, Max = 100)]
    [FormField(Group = "basic")]
    [TableColumn(Sortable = true)]
    public int Level { get; set; }

    [Select(typeof(PlayerRole))]
    [FormField(Group = "basic", Label = "Role")]
    [TableColumn(Sortable = true, Filterable = true)]
    public PlayerRole Role { get; set; }

    [TextArea(Rows = 3)]
    [FormField(Group = "contact", HelpText = "Tell us about yourself")]
    [TableIgnore]
    [ListIgnore]
    public string? Biography { get; set; }

    [CreateOnly]
    [ResponseIgnore]
    [PasswordInput]
    [TableIgnore]
    [MinLength(8)]
    public required string Password { get; set; }

    [FormConditional("Role", "Admin")]
    [FormField(Label = "Admin Notes")]
    [TableIgnore]
    public string? AdminNotes { get; set; }

    [Email]
    [FormField(Group = "contact", Label = "Email Address")]
    [TableColumn(Filterable = true)]
    public string? Email { get; set; }

    // Audit — auto-filled by generated store
    [DtoIgnore] [FormIgnore] public DateTime CreatedAt { get; set; }
    [DtoIgnore] [FormIgnore] public DateTime UpdatedAt { get; set; }
}
