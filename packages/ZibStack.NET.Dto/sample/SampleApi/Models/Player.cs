using System;
using ZibStack.NET.Core;
using ZibStack.NET.Dto;
using ZibStack.NET.TypeGen;
using ZibStack.NET.UI;
using ZibStack.NET.Validation;

namespace ZibStack.NET.Dto.Sample.Models;

// One model — generates: CRUD API + DTOs + Response + QueryDto + Form UI + Table UI + EF config + Audit
[CrudApi(Operations = CrudOperations.AllWithBulk)]
[UiForm]
[UiTable(DefaultSort = "Name", DefaultPageSize = 20)]
[UiFormGroup("basic", Label = "Basic Info", Order = 1)]
[UiFormGroup("contact", Label = "Contact", Order = 2)]
[ColumnPermission("Salary", "finance.read")]
[ZValidate]
public partial class Player
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    [UiFormIgnore]
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [ZRequired] [ZMinLength(2)] [ZMaxLength(50)]
    [UiFormField(Label = "Name", Placeholder = "Enter player name...", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public required string Name { get; set; }

    [ZRange(1, 100)]
    [UiFormField(Label = "Level", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public int Level { get; set; }

    [ZEmail]
    [UiFormField(Label = "Email", Placeholder = "player@example.com", Group = "contact")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public string? Email { get; set; }

    [UiFormField(Label = "Salary", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = false)]  // not filterable in query
    public decimal Salary { get; set; }

    [DtoIgnore(DtoTarget.List)]  // only in GET /api/players/{id}, not in list
    [UiFormField(Label = "Bio", Group = "basic")]
    [TextArea(Rows = 5)]
    [UiTableIgnore]
    public string? Bio { get; set; }

    [DtoOnly(DtoTarget.Create)]
    [DtoIgnore(DtoTarget.Response)]
    [ZMinLength(8)]
    [UiFormField(Label = "Password", Group = "contact")]
    [PasswordInput]
#pragma warning disable SDTO007     // required + [DtoIgnore(Response)] is intentional — passwords never ship to clients.
    public required string Password { get; set; }
#pragma warning restore SDTO007

    // ─── Relation: belongs to Team ──────────────────────────────────
    [UiFormField(Label = "Team", Group = "basic")]
    [UiTableColumn(Sortable = true)]
    public int? TeamId { get; set; }

    [DtoIgnore] [UiFormIgnore] [UiTableIgnore]
    [OneToOne]
    public Team? Team { get; set; }

    // Audit fields — auto-filled by generated store
    [DtoIgnore] [UiFormIgnore] public DateTime CreatedAt { get; set; }
    [DtoIgnore] [UiFormIgnore] public DateTime UpdatedAt { get; set; }
}
