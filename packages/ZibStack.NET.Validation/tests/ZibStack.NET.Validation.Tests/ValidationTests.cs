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
}
