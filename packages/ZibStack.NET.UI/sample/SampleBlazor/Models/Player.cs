using ZibStack.NET.Dto;
using ZibStack.NET.UI;
using ZibStack.NET.Validation;

namespace SampleBlazor.Models;

public enum PlayerRole
{
    Player,
    Moderator,
    Admin
}

[UiForm]
[UiTable(DefaultSort = "Name", DefaultPageSize = 20)]
[UiFormGroup("basic", Label = "Basic Info", Order = 1)]
[UiFormGroup("contact", Label = "Contact", Order = 2)]
public partial class Player
{
    [UiFormIgnore]
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [ZRequired]
    [ZMinLength(2)]
    [ZMaxLength(50)]
    [UiFormField(Label = "Player Name", Placeholder = "Enter name...", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [ZRange(1, 100)]
    [Slider(Min = 1, Max = 100)]
    [UiFormField(Group = "basic")]
    [UiTableColumn(Sortable = true)]
    public int Level { get; set; }

    [Select(typeof(PlayerRole))]
    [UiFormField(Group = "basic", Label = "Role")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public PlayerRole Role { get; set; }

    [ZMaxLength(500)]
    [TextArea(Rows = 3)]
    [UiFormField(Group = "contact", HelpText = "Tell us about yourself")]
    [UiTableIgnore]
    public string? Biography { get; set; }

    [ZRequired]
    [ZMinLength(6)]
    [DtoOnly(DtoTarget.Create)]
    [PasswordInput]
    [UiTableIgnore]
    public required string Password { get; set; }

    [UiFormConditional("Role", "Admin")]
    [UiFormField(Label = "Admin Notes")]
    [UiTableIgnore]
    public string? AdminNotes { get; set; }

    [DtoOnly(DtoTarget.Create)]
    [DatePicker]
    [UiFormField(Group = "basic")]
    [UiTableColumn(Sortable = true, Format = "yyyy-MM-dd")]
    public DateTime CreatedAt { get; set; }

    [ZEmail]
    [UiFormField(Group = "contact", Label = "Email Address")]
    [UiTableColumn(Filterable = true)]
    public string? Email { get; set; }
}
