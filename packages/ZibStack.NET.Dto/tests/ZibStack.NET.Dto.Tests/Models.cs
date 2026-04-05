using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Tests;

// ─── Separate mode (default) ───────────────────────────────────────

[CreateDto]
[UpdateDto]
[ResponseDto]
public class Product
{
    [DtoIgnore]
    public int Id { get; set; }

    public required string Name { get; set; }
    public decimal Price { get; set; }
    public string? Description { get; set; }
    public int Stock { get; set; }

    [CreateOnly]
    public required string Sku { get; set; }

    [UpdateOnly]
    public string? DiscontinuedReason { get; set; }

    [DtoIgnore]
    public DateTime CreatedAt { get; set; }
}

// ─── Combined mode ─────────────────────────────────────────────────

[CreateOrUpdateDto]
public class Category
{
    [DtoIgnore]
    public int Id { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}

// ─── Custom names ──────────────────────────────────────────────────

[CreateDto(Name = "NewItemDto")]
[UpdateDto(Name = "EditItemDto")]
public class Item
{
    public required string Title { get; set; }
    public int Quantity { get; set; }
}

[CreateOrUpdateDto(Name = "TagDto")]
public class Tag
{
    public required string Label { get; set; }
    public string? Color { get; set; }
}

// ─── External type (DtoFor) ────────────────────────────────────────

public class ExternalConfig
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
}

[CreateDtoFor(typeof(ExternalConfig), Ignore = new[] { "Id" }, Required = new[] { "Key", "Value" })]
public partial record CreateConfigRequest;

[UpdateDtoFor(typeof(ExternalConfig), Ignore = new[] { "Id", "IsSecret" })]
public partial record UpdateConfigRequest;

// ─── RenameProperty ────────────────────────────────────────────────

public class ExternalUser
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

[CreateDtoFor(typeof(ExternalUser), Ignore = new[] { "Id", "LastName" })]
[RenameProperty("FirstName", "Name")]
public partial record CreateUserRequest;

[UpdateDtoFor(typeof(ExternalUser), Ignore = new[] { "Id", "LastName" })]
[RenameProperty("FirstName", "Name")]
public partial record UpdateUserRequest;

// ─── PartialFrom ───────────────────────────────────────────────────

[PartialFrom(typeof(Product))]
public partial record PartialProduct;

// ─── IntersectFrom ─────────────────────────────────────────────────

public class Audit
{
    public string ModifiedBy { get; set; } = string.Empty;
    public DateTime ModifiedAt { get; set; }
}

[IntersectFrom(typeof(Category))]
[IntersectFrom(typeof(Audit))]
public partial record CategoryWithAudit;

// ─── Validation attribute propagation ──────────────────────────────

[CreateDto]
[ResponseDto]
public class ValidatedModel
{
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    [System.ComponentModel.DataAnnotations.EmailAddress]
    public required string Email { get; set; }

    [System.ComponentModel.DataAnnotations.Range(1, 999)]
    public int Quantity { get; set; }
}

// ─── All optional (no required) ────────────────────────────────────

[CreateDto]
public class Settings
{
    public string? Theme { get; set; }
    public int FontSize { get; set; }
    public bool DarkMode { get; set; }
}

// ─── Generic model ─────────────────────────────────────────────────

[CreateDto]
[UpdateDto]
public class Wrapper<T>
{
    public required T Value { get; set; }
    public string? Label { get; set; }
}

// ─── Create only / Update only ─────────────────────────────────────

[CreateDto]
public class CreateOnlyModel
{
    public required string Name { get; set; }
    public int Value { get; set; }
}

[UpdateDto]
public class UpdateOnlyModel
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}
