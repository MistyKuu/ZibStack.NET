using ZibStack.NET.Core;
using ZibStack.NET.UI;

namespace ZibStack.NET.UI.Tests;

public enum PlayerRole
{
    Player,
    Moderator,
    Admin
}

public enum Difficulty
{
    Easy,
    Medium,
    Hard,
    Expert
}

[UiForm]
[UiTable(DefaultSort = "Name", DefaultPageSize = 25)]
[UiFormGroup("basic", Label = "Basic Info", Order = 1)]
[UiFormGroup("contact", Label = "Contact", Order = 2)]
public partial class Player
{
    [UiFormIgnore]
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [UiFormField(Label = "Player Name", Placeholder = "Enter name...", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [Slider(Min = 1, Max = 100)]
    [UiFormField(Group = "basic")]
    [UiTableColumn(Sortable = true)]
    public int Level { get; set; }

    [Select(typeof(PlayerRole))]
    [UiFormField(Group = "basic", Label = "Role")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public PlayerRole Role { get; set; }

    [TextArea(Rows = 5)]
    [UiFormField(Group = "contact", HelpText = "Tell us about yourself")]
    [UiTableIgnore]
    public string? Biography { get; set; }

    [PasswordInput]
    [UiTableIgnore]
    public required string Password { get; set; }

    [UiFormConditional("Role", "Admin")]
    [UiFormField(Label = "Admin Notes")]
    [UiTableIgnore]
    public string? AdminNotes { get; set; }

    [DatePicker]
    [UiFormField(Group = "basic")]
    [UiTableColumn(Sortable = true, Format = "yyyy-MM-dd")]
    public DateTime CreatedAt { get; set; }

    [UiFormField(Group = "contact", Label = "Email Address")]
    [UiTableColumn(Filterable = true)]
    public string? Email { get; set; }
}

[UiForm(Name = "SimpleForm")]
public partial class SimpleModel
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public int Age { get; set; }
    public bool IsActive { get; set; }
}

[UiTable(DefaultPageSize = 10, DefaultSort = "Title")]
public partial class Article
{
    [UiTableColumn(Sortable = true, Filterable = true)]
    public string Title { get; set; } = "";

    [UiTableColumn(Sortable = true)]
    public string Author { get; set; } = "";

    [UiTableColumn(Sortable = true, Format = "yyyy-MM-dd")]
    public DateTime PublishedAt { get; set; }

    [UiTableColumn(Sortable = true, Filterable = true)]
    public Difficulty Difficulty { get; set; }

    [UiTableIgnore]
    public string Content { get; set; } = "";
}

[UiForm]
public partial class FormWithRadioAndFile
{
    [RadioGroup(typeof(Difficulty))]
    public Difficulty Level { get; set; }

    [FilePicker(Accept = "image/*", Multiple = true)]
    public string? Avatar { get; set; }

    [ColorPicker]
    public string? FavoriteColor { get; set; }

    [RichText]
    public string? Description { get; set; }

    [UiFormHidden]
    public string? InternalToken { get; set; }

    [UiFormReadOnly]
    public string? CreatedBy { get; set; }

    [UiFormDisabled]
    public string? LockedField { get; set; }
}

// ─── ERP models (backward compat with [ChildTable]) ─────────────────

public partial class CountyView { }

[UiTable(SchemaUrl = "/custom/postalcodes")]
public partial class PostalCodeView { }

[UiTable(DefaultSort = "Name", DefaultPageSize = 50)]
[Permission("voivodeship.read")]
[ColumnPermission("Budget", "finance.read")]
[DataFilter("VoivodeshipId")]
[ChildTable(typeof(CountyView), ForeignKey = "VoivodeshipId", Label = "Powiaty")]
[ChildTable(typeof(PostalCodeView), ForeignKey = "VoivodeshipId", Label = "Kody pocztowe")]
[RowAction("showDetails", Label = "Szczegóły", Endpoint = "/api/voivodeships/{id}")]
[RowAction("generateReport", Label = "Raport", Icon = "file",
           Endpoint = "/api/voivodeships/{id}/report", Method = "POST",
           Confirmation = "Wygenerować raport?")]
[ToolbarAction("export", Label = "Eksport", Icon = "download",
               Endpoint = "/api/voivodeships/export", SelectionMode = "multiple")]
[ToolbarAction("recalculate", Label = "Przelicz salda",
               Endpoint = "/api/voivodeships/recalculate", Method = "POST",
               Confirmation = "Przeliczyć salda?", Permission = "finance.write")]
public partial class VoivodeshipView
{
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [UiTableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";

    [UiTableColumn(Sortable = true)]
    public string Code { get; set; } = "";

    [UiTableColumn(Sortable = true)]
    [Computed]
    [ColumnStyle(When = "value < 0", Severity = "danger")]
    [ColumnStyle(When = "value >= 0", Severity = "success")]
    public decimal Budget { get; set; }

    [UiTableColumn(Sortable = true)]
    [Computed]
    public int CountyCount { get; set; }

    public int VoivodeshipId { get; set; }
}

// ─── Relationship models ([OneToMany] / [OneToOne]) ─────────────────

[UiTable(SchemaUrl = "/api/tables/task")]
[UiForm]
[Entity]
public partial class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int ProjectId { get; set; }
}

[UiTable(SchemaUrl = "/api/tables/attachment")]
[Entity]
public partial class Attachment
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public int ProjectId { get; set; }
}

[UiForm]
[Entity]
public partial class ProjectSettings
{
    public int Id { get; set; }
    public string Theme { get; set; } = "";
    public int ProjectId { get; set; }
}

[UiForm]
[UiTable(DefaultSort = "Name", SchemaUrl = "/api/tables/project")]
[Entity(TableName = "Projects")]
public partial class ProjectView
{
    [UiFormIgnore]
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [UiFormField(Label = "Project Name")]
    [UiTableColumn(Sortable = true)]
    public string Name { get; set; } = "";

    public int SettingsId { get; set; }

    [Computed]
    public int TaskCount { get; set; }

    [OneToMany(Label = "Tasks")]
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();

    [OneToMany(ForeignKey = nameof(Attachment.ProjectId), Label = "Attachments")]
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

    [OneToOne(Label = "Settings")]
    public ProjectSettings? Settings { get; set; }
}
