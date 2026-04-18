using System.Linq.Expressions;
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

// ── Cross-field validation with b.Rule() ──────────────────────────────────────

[ZValidate]
public partial class DateRangeRequest : IValidationConfigurator<DateRangeRequest>
{
    [ZRequired]
    public DateTime StartDate { get; set; }

    [ZRequired]
    public DateTime EndDate { get; set; }

    public void Configure(IValidationBuilder<DateRangeRequest> b)
    {
        b.Rule(x => x.EndDate > x.StartDate, "EndDate must be after StartDate");
    }
}

// ── Cross-field validation with b.Property().EqualTo() ────────────────────────

[ZValidate]
public partial class PasswordForm : IValidationConfigurator<PasswordForm>
{
    [ZRequired]
    [ZMinLength(8)]
    public string Password { get; set; } = "";

    [ZRequired]
    public string ConfirmPassword { get; set; } = "";

    public void Configure(IValidationBuilder<PasswordForm> b)
    {
        b.Property(x => x.ConfirmPassword).EqualTo(x => x.Password, "Passwords must match");
    }
}

// ── Mixed: per-property attrs + cross-field fluent ────────────────────────────

[ZValidate]
public partial class RangeConfig : IValidationConfigurator<RangeConfig>
{
    [ZRange(0, 1000)]
    public int Min { get; set; }

    [ZRange(0, 1000)]
    public int Max { get; set; }

    public void Configure(IValidationBuilder<RangeConfig> b)
    {
        b.Property(x => x.Max).GreaterThanOrEqual(x => x.Min);
    }
}
