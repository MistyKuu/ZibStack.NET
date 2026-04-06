using ZibStack.NET.Validation;

namespace ZibStack.NET.Validation.Tests;

[Validate]
public partial class CreateUserRequest
{
    [Required]
    [MinLength(2)]
    [MaxLength(50)]
    public string Name { get; set; } = "";

    [Required]
    [Email]
    public string Email { get; set; } = "";

    [Range(18, 120)]
    public int Age { get; set; }

    [Url]
    public string? Website { get; set; }

    [Match(@"^\+?\d{7,15}$", Message = "Invalid phone number format.")]
    public string? Phone { get; set; }
}

[Validate]
public partial class TeamRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(100)]
    public string TeamName { get; set; } = "";

    [NotEmpty]
    public List<string> Members { get; set; } = new();
}

[Validate]
public partial record ProductRecord
{
    [Required]
    public string Sku { get; init; } = "";

    [Range(0, 999999)]
    public decimal Price { get; init; }

    [MaxLength(500)]
    public string? Description { get; init; }
}
