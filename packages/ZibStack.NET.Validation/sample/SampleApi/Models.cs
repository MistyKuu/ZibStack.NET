using ZibStack.NET.Validation;

namespace SampleApi.Models;

// ══════════════════════════════════════════════════════════════════════════════
// E-commerce order — demonstrates ALL validation features in one model tree
// ══════════════════════════════════════════════════════════════════════════════

// ── Address (nested, reused in multiple places) ─────────────────────────────

[ZValidate]
public partial class Address
{
    [ZRequired]
    [ZMinLength(3)]
    public string Street { get; set; } = "";

    [ZRequired]
    public string City { get; set; } = "";

    [ZRequired]
    [ZMatch(@"^\d{5}(-\d{4})?$", Message = "Zip must be 5 digits (or 5+4 format)")]
    public string Zip { get; set; } = "";

    [ZPhone]
    public string? Phone { get; set; }
}

// ── Payment info ────────────────────────────────────────────────────────────

[ZValidate]
public partial class PaymentInfo
{
    [ZRequired]
    [ZIn("visa", "mastercard", "amex", "discover")]
    public string CardType { get; set; } = "";

    [ZRequired]
    [ZCreditCard]
    public string CardNumber { get; set; } = "";

    [ZRequired]
    [ZMatch(@"^\d{2}/\d{2}$", Message = "Expiry must be MM/YY format")]
    public string Expiry { get; set; } = "";

    [ZRange(100, 9999)]
    public int Cvv { get; set; }
}

// ── Line item (in collection) ───────────────────────────────────────────────

[ZValidate]
public partial class OrderItem : IValidationConfigurator<OrderItem>
{
    [ZRequired]
    [ZMinLength(3)]
    [ZMaxLength(50)]
    public string Sku { get; set; } = "";

    [ZRequired]
    public string ProductName { get; set; } = "";

    [ZRange(1, 999)]
    public int Quantity { get; set; }

    [ZRange(0.01, 99999.99)]
    public decimal UnitPrice { get; set; }

    public decimal Discount { get; set; }

    public void Configure(IValidationBuilder<OrderItem> b)
    {
        b.Rule(x => x.Discount >= 0 && x.Discount <= x.UnitPrice * x.Quantity,
            "Discount cannot exceed item total");
    }
}

// ── Main order request (the big one) ────────────────────────────────────────

[ZValidate]
public partial class CreateOrderRequest : IValidationConfigurator<CreateOrderRequest>
{
    // ── Customer info ──
    [ZRequired]
    [ZMinLength(2)]
    [ZMaxLength(100)]
    public string CustomerName { get; set; } = "";

    [ZRequired]
    [ZEmail]
    public string CustomerEmail { get; set; } = "";

    [ZNotIn("test@test.com", "admin@example.com")]
    public string? NotificationEmail { get; set; }

    // ── Nested objects ──
    public Address BillingAddress { get; set; } = new();
    public Address? ShippingAddress { get; set; }  // null = same as billing

    // ── Nested collection ──
    [ZNotEmpty]
    public List<OrderItem> Items { get; set; } = new();

    // ── Payment ──
    public PaymentInfo? Payment { get; set; }  // null for free orders

    // ── Order metadata ──
    [ZIn("standard", "express", "overnight")]
    public string ShippingMethod { get; set; } = "standard";

    [ZRange(0, 100)]
    public decimal DiscountPercent { get; set; }

    public string? CouponCode { get; set; }
    public bool IsFreeOrder { get; set; }

    [ZMaxLength(500)]
    public string? Notes { get; set; }

    // ── Cross-field + conditional rules ──
    public void Configure(IValidationBuilder<CreateOrderRequest> b)
    {
        // Free orders don't need payment
        b.Unless(x => x.IsFreeOrder, then =>
        {
            then.Rule(x => x.Payment != null, "Payment is required for non-free orders");
        });

        // Express/overnight need shipping address
        b.When(x => x.ShippingMethod != "standard", then =>
        {
            then.Rule(x => x.ShippingAddress != null,
                "Shipping address required for express/overnight delivery");
        });

        // Coupon code format
        b.When(x => x.CouponCode != null, then =>
        {
            then.Rule(x => x.CouponCode!.Length >= 5 && x.CouponCode.Length <= 20,
                "Coupon code must be 5-20 characters");
            then.Rule(x => x.CouponCode!.All(char.IsLetterOrDigit),
                "Coupon code must be alphanumeric");
        });

        // Business rules
        b.Rule(x => x.Items.Count <= 50, "Maximum 50 items per order");
        b.Rule(x => x.DiscountPercent == 0 || x.CouponCode != null,
            "Discount requires a coupon code");

        // RuleSets for different operations
        b.RuleSet("Express", set =>
        {
            set.Rule(x => x.Items.Count <= 10, "Express orders limited to 10 items");
            set.Rule(x => x.ShippingAddress != null, "Express requires shipping address");
        });
    }
}

// ── Registration (cascade + placeholders demo) ──────────────────────────────

[ZValidate]
public partial class RegisterUserRequest : IValidationConfigurator<RegisterUserRequest>
{
    [ZRequired(Message = "{PropertyName} cannot be empty")]
    [ZMinLength(3, Message = "{PropertyName} must be at least 3 chars")]
    [ZMaxLength(30, Message = "{PropertyName} too long (max 30)")]
    [ZCascade]
    public string Username { get; set; } = "";

    [ZRequired]
    [ZEmail(Message = "'{PropertyValue}' is not a valid email")]
    public string Email { get; set; } = "";

    [ZRequired]
    [ZMinLength(8)]
    [ZCascade]
    public string Password { get; set; } = "";

    [ZRequired]
    public string ConfirmPassword { get; set; } = "";

    [ZRange(13, 120, Message = "You must be between {PropertyName} 13-120")]
    public int Age { get; set; }

    [ZUrl]
    public string? Website { get; set; }

    [ZIn("free", "pro", "enterprise")]
    public string Plan { get; set; } = "free";

    public void Configure(IValidationBuilder<RegisterUserRequest> b)
    {
        b.Property(x => x.ConfirmPassword)
            .EqualTo(x => x.Password, "Passwords must match");

        b.When(x => x.Plan == "enterprise", then =>
        {
            then.Rule(x => x.Website != null, "Enterprise plan requires a website");
        });
    }
}
