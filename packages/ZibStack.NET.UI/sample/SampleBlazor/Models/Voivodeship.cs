using ZibStack.NET.UI;

namespace SampleBlazor.Models;

[UiTable(DefaultSort = "Name")]
public partial class CountyView
{
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }
    public int VoivodeshipId { get; set; }
    [UiTableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";
    [UiTableColumn(Sortable = true)]
    public int Population { get; set; }
}

[UiTable(DefaultSort = "Code")]
public partial class PostalCodeView
{
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }
    public int VoivodeshipId { get; set; }
    [UiTableColumn(Sortable = true, Filterable = true)]
    public string Code { get; set; } = "";
    [UiTableColumn(Sortable = true)]
    public string City { get; set; } = "";
}

[UiForm]
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
[ToolbarAction("export", Label = "Eksport do Excel", Icon = "download",
               Endpoint = "/api/voivodeships/export", Method = "GET",
               SelectionMode = "multiple")]
[ToolbarAction("recalculate", Label = "Przelicz salda",
               Endpoint = "/api/voivodeships/recalculate", Method = "POST",
               Confirmation = "Przeliczyć salda dla wszystkich województw?",
               Permission = "finance.write")]
[UiFormGroup("basic", Label = "Dane podstawowe", Order = 1)]
[UiFormGroup("finance", Label = "Finanse", Order = 2)]
public partial class VoivodeshipView
{
    [UiFormIgnore][UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [UiFormField(Label = "Nazwa", Placeholder = "Nazwa województwa", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";

    [UiFormField(Label = "Kod", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public string Code { get; set; } = "";

    [UiFormField(Label = "Stolica", Group = "basic")]
    [UiTableColumn(Sortable = true)]
    public string Capital { get; set; } = "";

    [UiFormIgnore]
    [UiTableColumn(Sortable = true, Label = "Budżet")]
    [Computed]
    [ColumnStyle(When = "value < 0", Severity = "danger")]
    [ColumnStyle(When = "value >= 0", Severity = "success")]
    public decimal Budget { get; set; }

    [UiFormIgnore]
    [UiTableColumn(Sortable = true, Label = "Liczba powiatów")]
    [Computed]
    public int CountyCount { get; set; }

    [UiFormField(Label = "Populacja", Group = "basic")]
    [UiTableColumn(Sortable = true, Format = "N0")]
    public int Population { get; set; }

    [UiFormField(Label = "Notatki", Group = "finance")]
    [TextArea(Rows = 3)]
    [UiTableIgnore]
    public string? Notes { get; set; }
}
