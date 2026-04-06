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

[Form]
[Table(DefaultSort = "Name", DefaultPageSize = 25)]
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

    [TextArea(Rows = 5)]
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

[Form(Name = "SimpleForm")]
public partial class SimpleModel
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public int Age { get; set; }
    public bool IsActive { get; set; }
}

[Table(DefaultPageSize = 10, DefaultSort = "Title")]
public partial class Article
{
    [TableColumn(Sortable = true, Filterable = true)]
    public string Title { get; set; } = "";

    [TableColumn(Sortable = true)]
    public string Author { get; set; } = "";

    [TableColumn(Sortable = true, Format = "yyyy-MM-dd")]
    public DateTime PublishedAt { get; set; }

    [TableColumn(Sortable = true, Filterable = true)]
    public Difficulty Difficulty { get; set; }

    [TableIgnore]
    public string Content { get; set; } = "";
}

[Form]
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

    [FormHidden]
    public string? InternalToken { get; set; }

    [FormReadOnly]
    public string? CreatedBy { get; set; }

    [FormDisabled]
    public string? LockedField { get; set; }
}

// ─── ERP models (backward compat with [ChildTable]) ─────────────────

public partial class CountyView { }

[Table(SchemaUrl = "/custom/postalcodes")]
public partial class PostalCodeView { }

[Table(DefaultSort = "Name", DefaultPageSize = 50)]
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
    [TableColumn(IsVisible = false)]
    public int Id { get; set; }

    [TableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";

    [TableColumn(Sortable = true)]
    public string Code { get; set; } = "";

    [TableColumn(Sortable = true)]
    [Computed]
    [ColumnStyle(When = "value < 0", Severity = "danger")]
    [ColumnStyle(When = "value >= 0", Severity = "success")]
    public decimal Budget { get; set; }

    [TableColumn(Sortable = true)]
    [Computed]
    public int CountyCount { get; set; }

    public int VoivodeshipId { get; set; }
}

// ─── Relationship models ([OneToMany] / [OneToOne]) ─────────────────

[Table(SchemaUrl = "/api/tables/task")]
[Form]
public partial class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int ProjectId { get; set; }
}

[Table(SchemaUrl = "/api/tables/attachment")]
public partial class Attachment
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public int ProjectId { get; set; }
}

[Form]
public partial class ProjectSettings
{
    public int Id { get; set; }
    public string Theme { get; set; } = "";
    public int ProjectId { get; set; }
}

[Form]
[Table(DefaultSort = "Name", SchemaUrl = "/api/tables/project")]
public partial class ProjectView
{
    [FormIgnore]
    [TableColumn(IsVisible = false)]
    public int Id { get; set; }

    [FormField(Label = "Project Name")]
    [TableColumn(Sortable = true)]
    public string Name { get; set; } = "";

    public int SettingsId { get; set; }

    [OneToMany(Label = "Tasks")]
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();

    [OneToMany(ForeignKey = nameof(Attachment.ProjectId), Label = "Attachments")]
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

    [OneToOne(Label = "Settings")]
    public ProjectSettings? Settings { get; set; }
}
