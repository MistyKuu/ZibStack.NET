using ZibStack.NET.UI;

namespace SampleBlazor.Models;

public enum PlayerRole
{
    Player,
    Moderator,
    Admin
}

[Form]
[Table(DefaultSort = "Name", DefaultPageSize = 20)]
[FormGroup("basic", Label = "Basic Info", Order = 1)]
[FormGroup("contact", Label = "Contact", Order = 2)]
public partial class Player
{
    [FormIgnore]
    [TableColumn(IsVisible = false)]
    public int Id { get; set; }

    [FormField(Label = "Player Name", Placeholder = "Enter name...", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

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
    public string? Biography { get; set; }

    [PasswordInput]
    [TableIgnore]
    public required string Password { get; set; }

    [FormConditional("Role", "Admin")]
    [FormField(Label = "Admin Notes")]
    [TableIgnore]
    public string? AdminNotes { get; set; }

    [DatePicker]
    [FormField(Group = "basic")]
    [TableColumn(Sortable = true, Format = "yyyy-MM-dd")]
    public DateTime CreatedAt { get; set; }

    [FormField(Group = "contact", Label = "Email Address")]
    [TableColumn(Filterable = true)]
    public string? Email { get; set; }
}
