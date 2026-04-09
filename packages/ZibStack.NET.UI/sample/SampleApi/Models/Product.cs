using ZibStack.NET.Dto;
using ZibStack.NET.UI;
using ZibStack.NET.Validation;

namespace SampleApi.Models;

public enum ProductCategory
{
    Electronics,
    Clothing,
    Food,
    Books,
    Other
}

// ─── One attribute. Everything generated. ────────────────────────────
//
// [ImTiredOfCrud] generates:
//   CRUD API:  GET/POST/PATCH/DELETE /api/products  (with filter/sort/select/pagination)
//   DTOs:      CreateProductRequest, UpdateProductRequest, ProductResponse, ProductQuery
//   Form:      GET /api/forms/product   → JSON schema with apiUrl, keyProperty, validation, groups
//   Table:     GET /api/tables/product  → JSON schema with apiUrl, keyProperty, filterOperators per column
//   Validation: compile-time Validate() from [ZRequired], [Range], etc.
//   Query DSL: ?filter=Price>100,Category=Electronics&sort=-Price&select=Name,Price
//
// A frontend reads the table/form schemas and gets everything it needs to render
// a smart data grid with filtering/sorting and a form with submit — zero configuration.

[ImTiredOfCrud(DefaultSort = "Name")]
[UiFormGroup("basic", Label = "Basic Info", Order = 1)]
[UiFormGroup("pricing", Label = "Pricing", Order = 2)]
public partial class Product
{
    [UiFormIgnore]
    [UiTableColumn(IsVisible = false)]
    public int Id { get; set; }

    [ZRequired] [ZMaxLength(200)]
    [UiFormField(Label = "Product Name", Placeholder = "Enter product name...", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public string Name { get; set; } = "";

    [ZMaxLength(1000)]
    [UiFormField(Label = "Description", Group = "basic")]
    [TextArea(Rows = 3)]
    [UiTableColumn(Filterable = true)]
    public string? Description { get; set; }

    [Select(typeof(ProductCategory))]
    [UiFormField(Label = "Category", Group = "basic")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public ProductCategory Category { get; set; }

    [ZRange(0, 999999)]
    [UiFormField(Label = "Price", Group = "pricing")]
    [UiTableColumn(Sortable = true, Filterable = true, Format = "currency")]
    public decimal Price { get; set; }

    [ZRange(0, 10000)]
    [UiFormField(Label = "Stock", Group = "pricing")]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public int Stock { get; set; }

    [UiFormField(Label = "Active")]
    [Checkbox]
    [UiTableColumn(Sortable = true, Filterable = true)]
    public bool IsActive { get; set; } = true;

    // Audit
    [DtoIgnore] [UiFormIgnore] public DateTime CreatedAt { get; set; }
    [DtoIgnore] [UiFormIgnore] public DateTime UpdatedAt { get; set; }
}
