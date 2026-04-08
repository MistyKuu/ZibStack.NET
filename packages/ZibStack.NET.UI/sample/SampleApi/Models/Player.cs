using ZibStack.NET.UI;
using ZibStack.NET.Validation;

namespace SampleApi.Models;

public enum PlayerRole
{
    Player,
    Moderator,
    Admin
}

// ─── UI metadata demo — form + table schemas from attributes ────────

[UiForm]
[UiTable(DefaultSort = "Name", DefaultPageSize = 20)]
[UiFormGroup("basic", Label = "Basic Info", Order = 1)]
[UiFormGroup("contact", Label = "Contact", Order = 2)]
public partial class Player
{
    [UiFormIgnore]
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [Required] [MinLength(2)] [MaxLength(50)]
    [UiFormField(Label = "Player Name", Placeholder = "Enter name...", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [Range(1, 100)]
    [Slider(Min = 1, Max = 100)]
    [UiFormField(Group = "basic")]
    [UiTableColumn(Sortable = true)]
    public int Level { get; set; }

    [Select(typeof(PlayerRole))]
    [UiFormField(Group = "basic", Label = "Role")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public PlayerRole Role { get; set; }

    [TextArea(Rows = 3)]
    [UiFormField(Group = "contact", HelpText = "Tell us about yourself")]
    [UiTableIgnore]
    public string? Biography { get; set; }

    [UiFormConditional("Role", "Admin")]
    [UiFormField(Label = "Admin Notes")]
    [UiTableIgnore]
    public string? AdminNotes { get; set; }

    [Email]
    [UiFormField(Group = "contact", Label = "Email Address")]
    [UiTableColumn(Filterable = true)]
    public string? Email { get; set; }
}
