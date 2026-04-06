using ZibStack.NET.UI;

namespace SampleApi.Models;

// ─── ERP-style hierarchical views ────────────────────────────────────

[Table(DefaultSort = "Name")]
public partial class CountyView
{
    [TableColumn(IsVisible = false)]
    public int Id { get; set; }

    public int VoivodeshipId { get; set; }

    [TableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";

    [TableColumn(Sortable = true)]
    public int Population { get; set; }
}

[Table(DefaultSort = "Code")]
public partial class PostalCodeView
{
    [TableColumn(IsVisible = false)]
    public int Id { get; set; }

    public int VoivodeshipId { get; set; }

    [TableColumn(Sortable = true, Filterable = true)]
    public string Code { get; set; } = "";

    [TableColumn(Sortable = true)]
    public string City { get; set; } = "";
}

[Form]
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
[ToolbarAction("export", Label = "Eksport do Excel", Icon = "download",
               Endpoint = "/api/voivodeships/export", Method = "GET",
               SelectionMode = "multiple")]
[ToolbarAction("recalculate", Label = "Przelicz salda",
               Endpoint = "/api/voivodeships/recalculate", Method = "POST",
               Confirmation = "Przeliczyć salda dla wszystkich województw?",
               Permission = "finance.write")]
[FormGroup("basic", Label = "Dane podstawowe", Order = 1)]
[FormGroup("finance", Label = "Finanse", Order = 2)]
public partial class VoivodeshipView
{
    [FormIgnore]
    [TableColumn(IsVisible = false)]
    public int Id { get; set; }

    [FormField(Label = "Nazwa", Placeholder = "Nazwa województwa", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";

    [FormField(Label = "Kod", Group = "basic")]
    [TableColumn(Sortable = true, Filterable = true)]
    public string Code { get; set; } = "";

    [FormField(Label = "Stolica", Group = "basic")]
    [TableColumn(Sortable = true)]
    public string Capital { get; set; } = "";

    [FormIgnore]
    [TableColumn(Sortable = true, Label = "Budżet")]
    [Computed]
    [ColumnStyle(When = "value < 0", Severity = "danger")]
    [ColumnStyle(When = "value >= 0", Severity = "success")]
    public decimal Budget { get; set; }

    [FormIgnore]
    [TableColumn(Sortable = true, Label = "Liczba powiatów")]
    [Computed]
    public int CountyCount { get; set; }

    [FormField(Label = "Populacja", Group = "basic")]
    [TableColumn(Sortable = true, Format = "N0")]
    public int Population { get; set; }

    [FormField(Label = "Notatki", Group = "finance")]
    [TextArea(Rows = 3)]
    [TableIgnore]
    public string? Notes { get; set; }
}
