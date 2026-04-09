using ZibStack.NET.Validation;

namespace ZibStack.NET.Validation.Tests;

[ZValidate]
public partial class CreateUserRequest
{
    [ZRequired]
    [ZMinLength(2)]
    [ZMaxLength(50)]
    public string Name { get; set; } = "";

    [ZRequired]
    [ZEmail]
    public string Email { get; set; } = "";

    [ZRange(18, 120)]
    public int Age { get; set; }

    [ZUrl]
    public string? Website { get; set; }

    [ZMatch(@"^\+?\d{7,15}$", Message = "Invalid phone number format.")]
    public string? Phone { get; set; }
}

[ZValidate]
public partial class TeamRequest
{
    [ZRequired]
    [ZMinLength(1)]
    [ZMaxLength(100)]
    public string TeamName { get; set; } = "";

    [ZNotEmpty]
    public List<string> Members { get; set; } = new();
}

[ZValidate]
public partial record ProductRecord
{
    [ZRequired]
    public string Sku { get; init; } = "";

    [ZRange(0, 999999)]
    public decimal Price { get; init; }

    [ZMaxLength(500)]
    public string? Description { get; init; }
}
