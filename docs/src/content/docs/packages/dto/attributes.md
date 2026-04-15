---
title: Attributes reference
description: Class-level and property-level Dto attributes — what each does, when to use it.
---

## Attributes

**Class-level attributes:**

| Attribute | Target | Description |
|-----------|--------|-------------|
| `[CreateDto]` | Class | Generates Create request with `Validate()` + `ToEntity()` |
| `[UpdateDto]` | Class | Generates Update request with `Validate()` + `ApplyTo()` |
| `[CreateOrUpdateDto]` | Class | Generates single DTO with `ValidateForCreate/Update()` + both |
| `[ResponseDto]` | Class | Generates read-only Response DTO with `FromEntity()` + `ProjectFrom()` |
| `[QueryDto]` | Class | Generates filter + sort DTO with nullable properties + `ApplyFilter/ApplySort/Apply(IQueryable)`. `Sortable` defaults to `true`. |
| `[QueryDto(Sortable = false)]` | Class | Filter-only DTO — skips `SortBy`, `SortDirection`, `ApplySort()`. For endpoints with a fixed result order. |
| `[CrudApi]` | Class | Generates full CRUD API endpoints + auto-implies missing DTOs |
| `[CreateDtoFor(typeof(T))]` | Record (partial) | Generates create DTO for external type `T` |
| `[UpdateDtoFor(typeof(T))]` | Record (partial) | Generates update DTO for external type `T` |

**Property-level attributes:**

| Attribute | Description |
|-----------|-------------|
| `[DtoIgnore]` | Excludes from **all** generated DTOs (equivalent to `DtoTarget.All`) |
| `[DtoIgnore(DtoTarget.X)]` | Excludes from specific DTO targets: `Create`, `Update`, `Response`, `Query`, `List` (combinable with `\|`) |
| `[DtoOnly(DtoTarget.X)]` | Includes **only** in the specified DTO target(s): e.g. `DtoTarget.Create`, `DtoTarget.Update` |
| `[DtoName("json_name")]` | Overrides the JSON property name (works on all DTOs including Response) |
| `[Flatten]` | Expands nested object properties into parent DTO (Response only) |

> **Set-once fields:** Use `[DtoIgnore(DtoTarget.Update)]` for properties that can be set at creation but never changed — the property won't appear in the PATCH DTO at all.

**`[DtoName]` on Response DTO:**

```csharp
[CrudApi]
public partial class Order
{
    [DtoName("Customer")]
    public string CustomerName { get; set; }  // → "Customer" in response

    [DtoName("Total")]
    public decimal TotalAmount { get; set; }  // → "Total" in response
}
```

**Generated types:**

| Type | Description |
|------|-------------|
| `PaginatedResponse<T>` | Generic paginated wrapper with `Items`, `TotalCount`, `Page`, `PageSize` |
| `DtoValidationResult` | Per-property validation errors with `IsValid`, `Errors`, `AddError()`, `Merge()`, `ToDictionary()` |

> **Note:** `[PartialFrom]`, `[IntersectFrom]`, `[PickFrom]`, `[OmitFrom]` are in the separate [`ZibStack.NET.Core`](https://www.nuget.org/packages/ZibStack.NET.Core) package.

