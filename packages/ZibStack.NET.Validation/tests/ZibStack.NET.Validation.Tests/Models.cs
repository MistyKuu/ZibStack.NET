using System.Linq.Expressions;
using ZibStack.NET.Validation;

// ReSharper disable UnusedAutoPropertyAccessor.Global

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

// ── Complex cross-field lambdas ───────────────────────────────────────────────

[ZValidate]
public partial class OrderRequest : IValidationConfigurator<OrderRequest>
{
    [ZRequired]
    public string Customer { get; set; } = "";

    public List<string> Items { get; set; } = new();

    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }

    public DateTime? ShipBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public void Configure(IValidationBuilder<OrderRequest> b)
    {
        b.Rule(x => x.Items.Count > 0, "Order must have at least one item");
        b.Rule(x => x.Discount >= 0 && x.Discount <= x.Subtotal, "Discount cannot exceed subtotal");
        b.Rule(x => x.Total == x.Subtotal - x.Discount, "Total must equal Subtotal minus Discount");
        b.Rule(x => x.ShipBy == null || x.ShipBy > x.CreatedAt, "ShipBy must be after CreatedAt");
    }
}

// ── Nested validation ─────────────────────────────────────────────────────────

[ZValidate]
public partial class Address
{
    [ZRequired]
    public string Street { get; set; } = "";

    [ZRequired]
    public string City { get; set; } = "";

    [ZMatch(@"^\d{2}-\d{3}$", Message = "Zip must be XX-XXX format")]
    public string? Zip { get; set; }
}

[ZValidate]
public partial class CustomerForm
{
    [ZRequired]
    public string Name { get; set; } = "";

    public Address BillingAddress { get; set; } = new();
    public Address? ShippingAddress { get; set; }
}

// ── Nested collection validation ──────────────────────────────────────────────

[ZValidate]
public partial class LineItem
{
    [ZRequired]
    public string Sku { get; set; } = "";

    [ZRange(1, 9999)]
    public int Quantity { get; set; }
}

[ZValidate]
public partial class Invoice
{
    [ZRequired]
    public string InvoiceNumber { get; set; } = "";

    public List<LineItem> Lines { get; set; } = new();
}

// ── [ZIn] / [ZNotIn] test ────────────────────────────────────────────────────

[ZValidate]
public partial class StatusRequest : IValidationConfigurator<StatusRequest>
{
    [ZIn("draft", "active", "archived")]
    public string Status { get; set; } = "";

    [ZNotIn("admin", "root")]
    public string Username { get; set; } = "";

    public void Configure(IValidationBuilder<StatusRequest> b) { }
}

// ── [ZCreditCard] / [ZPhone] test ────────────────────────────────────────────

[ZValidate]
public partial class PaymentRequest
{
    [ZRequired] [ZCreditCard]
    public string CardNumber { get; set; } = "";

    [ZPhone]
    public string? Phone { get; set; }
}

// ── Conditional validation test ──────────────────────────────────────────────

[ZValidate]
public partial class ShippingRequest : IValidationConfigurator<ShippingRequest>
{
    public bool RequiresShipping { get; set; }
    public string? ShippingAddress { get; set; }

    public void Configure(IValidationBuilder<ShippingRequest> b)
    {
        b.When(x => x.RequiresShipping, then =>
        {
            then.Rule(x => !string.IsNullOrEmpty(x.ShippingAddress), "Shipping address is required when shipping is needed");
        });
    }
}
