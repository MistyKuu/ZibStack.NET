using Xunit;
using ZibStack.NET.Validation;

namespace ZibStack.NET.Validation.Tests;

public class ValidationTests
{
    // ── Required ──────────────────────────────────────────────────────

    [Fact]
    public void Required_NullString_ReturnsError()
    {
        var request = new CreateUserRequest { Name = null!, Email = "test@test.com", Age = 25 };
        var result = request.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Name"));
    }

    [Fact]
    public void Required_EmptyString_ReturnsError()
    {
        var request = new CreateUserRequest { Name = "  ", Email = "test@test.com", Age = 25 };
        var result = request.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Name"));
    }

    [Fact]
    public void Required_ValidString_NoError()
    {
        var request = new CreateUserRequest { Name = "John", Email = "john@test.com", Age = 25 };
        var result = request.Validate();

        Assert.True(result.IsValid);
    }

    // ── MinLength / MaxLength ─────────────────────────────────────────

    [Fact]
    public void MinLength_TooShort_ReturnsError()
    {
        var request = new CreateUserRequest { Name = "J", Email = "j@t.com", Age = 25 };
        var result = request.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("at least 2"));
    }

    [Fact]
    public void MaxLength_TooLong_ReturnsError()
    {
        var request = new CreateUserRequest
        {
            Name = new string('A', 51),
            Email = "j@t.com",
            Age = 25
        };
        var result = request.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("at most 50"));
    }

    // ── Range ─────────────────────────────────────────────────────────

    [Fact]
    public void Range_BelowMin_ReturnsError()
    {
        var request = new CreateUserRequest { Name = "John", Email = "j@t.com", Age = 10 };
        var result = request.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("between"));
    }

    [Fact]
    public void Range_AboveMax_ReturnsError()
    {
        var request = new CreateUserRequest { Name = "John", Email = "j@t.com", Age = 200 };
        var result = request.Validate();

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Range_AtBoundary_NoError()
    {
        var request = new CreateUserRequest { Name = "John", Email = "j@t.com", Age = 18 };
        var result = request.Validate();

        Assert.True(result.IsValid);
    }

    // ── Email ─────────────────────────────────────────────────────────

    [Fact]
    public void Email_Invalid_ReturnsError()
    {
        var request = new CreateUserRequest { Name = "John", Email = "not-an-email", Age = 25 };
        var result = request.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("email"));
    }

    [Fact]
    public void Email_Valid_NoError()
    {
        var request = new CreateUserRequest { Name = "John", Email = "john@example.com", Age = 25 };
        var result = request.Validate();

        Assert.True(result.IsValid);
    }

    // ── Url ───────────────────────────────────────────────────────────

    [Fact]
    public void Url_Invalid_ReturnsError()
    {
        var request = new CreateUserRequest { Name = "John", Email = "j@t.com", Age = 25, Website = "not-a-url" };
        var result = request.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("URL"));
    }

    [Fact]
    public void Url_Valid_NoError()
    {
        var request = new CreateUserRequest { Name = "John", Email = "j@t.com", Age = 25, Website = "https://example.com" };
        var result = request.Validate();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Url_Null_NoError()
    {
        var request = new CreateUserRequest { Name = "John", Email = "j@t.com", Age = 25, Website = null };
        var result = request.Validate();

        Assert.True(result.IsValid);
    }

    // ── Match (Regex) ─────────────────────────────────────────────────

    [Fact]
    public void Match_Invalid_ReturnsCustomMessage()
    {
        var request = new CreateUserRequest { Name = "John", Email = "j@t.com", Age = 25, Phone = "abc" };
        var result = request.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Invalid phone number"));
    }

    [Fact]
    public void Match_Valid_NoError()
    {
        var request = new CreateUserRequest { Name = "John", Email = "j@t.com", Age = 25, Phone = "+48123456789" };
        var result = request.Validate();

        Assert.True(result.IsValid);
    }

    // ── NotEmpty ──────────────────────────────────────────────────────

    [Fact]
    public void NotEmpty_EmptyCollection_ReturnsError()
    {
        var request = new TeamRequest { TeamName = "Team A", Members = new List<string>() };
        var result = request.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("not be empty"));
    }

    [Fact]
    public void NotEmpty_WithItems_NoError()
    {
        var request = new TeamRequest { TeamName = "Team A", Members = new List<string> { "Alice" } };
        var result = request.Validate();

        Assert.True(result.IsValid);
    }

    // ── IValidatable ──────────────────────────────────────────────────

    [Fact]
    public void ImplementsIValidatable()
    {
        IValidatable validatable = new CreateUserRequest { Name = "John", Email = "j@t.com", Age = 25 };
        var result = validatable.Validate();

        Assert.True(result.IsValid);
    }

    // ── Record support ────────────────────────────────────────────────

    [Fact]
    public void Record_Validates()
    {
        var product = new ProductRecord { Sku = "ABC-123", Price = 9.99m };
        var result = product.Validate();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Record_Required_Empty_ReturnsError()
    {
        var product = new ProductRecord { Sku = "", Price = 9.99m };
        var result = product.Validate();

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Record_Range_Negative_ReturnsError()
    {
        var product = new ProductRecord { Sku = "ABC", Price = -1m };
        var result = product.Validate();

        Assert.False(result.IsValid);
    }

    // ── Multiple errors ───────────────────────────────────────────────

    [Fact]
    public void MultipleErrors_AllCollected()
    {
        var request = new CreateUserRequest { Name = "", Email = "bad", Age = 5 };
        var result = request.Validate();

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 3); // name required, email invalid, age out of range
    }

    // ── ValidationResult.Success singleton ────────────────────────────

    [Fact]
    public void ValidationResult_Success_IsValid()
    {
        Assert.True(ValidationResult.Success.IsValid);
        Assert.Empty(ValidationResult.Success.Errors);
    }

    // ── Cross-field: b.Rule() ────────────────────────────────────────

    [Fact]
    public void CrossField_Rule_EndAfterStart_Valid()
    {
        var req = new DateRangeRequest { StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) };
        Assert.True(req.Validate().IsValid);
    }

    [Fact]
    public void CrossField_Rule_EndBeforeStart_Invalid()
    {
        var req = new DateRangeRequest { StartDate = new(2026, 12, 31), EndDate = new(2026, 1, 1) };
        var result = req.Validate();
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("EndDate must be after StartDate"));
    }

    [Fact]
    public void CrossField_Rule_EqualDates_Invalid()
    {
        var req = new DateRangeRequest { StartDate = new(2026, 6, 1), EndDate = new(2026, 6, 1) };
        var result = req.Validate();
        Assert.False(result.IsValid);
    }

    // ── Cross-field: b.Property().EqualTo() ──────────────────────────

    [Fact]
    public void CrossField_EqualTo_MatchingPasswords_Valid()
    {
        var form = new PasswordForm { Password = "secret123", ConfirmPassword = "secret123" };
        Assert.True(form.Validate().IsValid);
    }

    [Fact]
    public void CrossField_EqualTo_MismatchedPasswords_Invalid()
    {
        var form = new PasswordForm { Password = "secret123", ConfirmPassword = "different" };
        var result = form.Validate();
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Passwords must match"));
    }

    // ── Cross-field: b.Property().GreaterThanOrEqual() ───────────────

    [Fact]
    public void CrossField_GreaterThanOrEqual_Valid()
    {
        var cfg = new RangeConfig { Min = 10, Max = 100 };
        Assert.True(cfg.Validate().IsValid);
    }

    [Fact]
    public void CrossField_GreaterThanOrEqual_Equal_Valid()
    {
        var cfg = new RangeConfig { Min = 50, Max = 50 };
        Assert.True(cfg.Validate().IsValid);
    }

    [Fact]
    public void CrossField_GreaterThanOrEqual_MaxLessThanMin_Invalid()
    {
        var cfg = new RangeConfig { Min = 100, Max = 10 };
        var result = cfg.Validate();
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Max") && e.Contains("Min"));
    }

    // ── Cross-field + per-property combined ──────────────────────────

    [Fact]
    public void CrossField_Combined_PerPropertyAndCrossField_BothFail()
    {
        var cfg = new RangeConfig { Min = 5000, Max = 2 }; // Min out of [ZRange(0,1000)] AND Max < Min
        var result = cfg.Validate();
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 2); // at least ZRange error + cross-field error
    }

    // ── Complex lambda expressions ──────────────────────────────────

    [Fact]
    public void CrossField_ComplexLambda_EmptyItems_Invalid()
    {
        var order = new OrderRequest { Customer = "Bob", Items = new(), Subtotal = 100, Discount = 0, Total = 100 };
        var result = order.Validate();
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("at least one item"));
    }

    [Fact]
    public void CrossField_ComplexLambda_WithItems_Valid()
    {
        var order = new OrderRequest { Customer = "Bob", Items = new() { "Widget" }, Subtotal = 100, Discount = 10, Total = 90 };
        var result = order.Validate();
        Assert.True(result.IsValid);
    }

    [Fact]
    public void CrossField_ComplexLambda_DiscountExceedsSubtotal_Invalid()
    {
        var order = new OrderRequest { Customer = "Bob", Items = new() { "Widget" }, Subtotal = 100, Discount = 150, Total = -50 };
        var result = order.Validate();
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Discount cannot exceed subtotal"));
    }

    [Fact]
    public void CrossField_ComplexLambda_TotalMismatch_Invalid()
    {
        var order = new OrderRequest { Customer = "Bob", Items = new() { "Widget" }, Subtotal = 100, Discount = 10, Total = 999 };
        var result = order.Validate();
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Total must equal"));
    }

    [Fact]
    public void CrossField_ComplexLambda_ShipByBeforeCreated_Invalid()
    {
        var order = new OrderRequest
        {
            Customer = "Bob", Items = new() { "Widget" },
            Subtotal = 100, Discount = 0, Total = 100,
            CreatedAt = new DateTime(2026, 6, 1),
            ShipBy = new DateTime(2026, 1, 1), // before CreatedAt
        };
        var result = order.Validate();
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("ShipBy must be after"));
    }

    [Fact]
    public void CrossField_ComplexLambda_ShipByNull_Valid()
    {
        var order = new OrderRequest
        {
            Customer = "Bob", Items = new() { "Widget" },
            Subtotal = 50, Discount = 0, Total = 50,
            ShipBy = null, // null is OK per the rule
        };
        Assert.True(order.Validate().IsValid);
    }
}
