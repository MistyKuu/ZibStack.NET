using System.ComponentModel.DataAnnotations;

namespace ZibStack.NET.Dto.Tests;

public class ValidationPropagationTests
{
    [Fact]
    public void CreateDto_PropagatesMaxLengthAttribute()
    {
        var prop = typeof(CreateValidatedModelRequest).GetProperty("Email")!;
        var attr = prop.GetCustomAttributes(typeof(MaxLengthAttribute), false);
        Assert.Single(attr);
        Assert.Equal(100, ((MaxLengthAttribute)attr[0]).Length);
    }

    [Fact]
    public void CreateDto_PropagatesEmailAddressAttribute()
    {
        var prop = typeof(CreateValidatedModelRequest).GetProperty("Email")!;
        var attr = prop.GetCustomAttributes(typeof(EmailAddressAttribute), false);
        Assert.Single(attr);
    }

    [Fact]
    public void CreateDto_PropagatesRangeAttribute()
    {
        var prop = typeof(CreateValidatedModelRequest).GetProperty("Quantity")!;
        var attr = prop.GetCustomAttributes(typeof(RangeAttribute), false);
        Assert.Single(attr);
        var range = (RangeAttribute)attr[0];
        Assert.Equal(1, range.Minimum);
        Assert.Equal(999, range.Maximum);
    }

    [Fact]
    public void ResponseDto_PropagatesAttributes()
    {
        var prop = typeof(ValidatedModelResponse).GetProperty("Email")!;
        var maxLen = prop.GetCustomAttributes(typeof(MaxLengthAttribute), false);
        Assert.Single(maxLen);

        var email = prop.GetCustomAttributes(typeof(EmailAddressAttribute), false);
        Assert.Single(email);
    }

    // ─── Inline validation enforcement ──────────────────────────────

    [Fact]
    public void Validate_ValidData_ReturnsNoErrors()
    {
        var request = new CreateValidatedModelRequest
        {
            Email = new PatchField<string>("user@example.com"),
            Quantity = new PatchField<int>(10),
        };
        var errors = request.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_EmailTooLong_ReturnsMaxLengthError()
    {
        var longEmail = new string('a', 95) + "@test.com";
        var request = new CreateValidatedModelRequest
        {
            Email = new PatchField<string>(longEmail),
            Quantity = new PatchField<int>(5),
        };
        var errors = request.Validate();
        Assert.Contains(errors, e => e.Contains("at most 100"));
    }

    [Fact]
    public void Validate_InvalidEmail_ReturnsEmailError()
    {
        var request = new CreateValidatedModelRequest
        {
            Email = new PatchField<string>("not-an-email"),
            Quantity = new PatchField<int>(5),
        };
        var errors = request.Validate();
        Assert.Contains(errors, e => e.Contains("email"));
    }

    [Fact]
    public void Validate_QuantityBelowRange_ReturnsRangeError()
    {
        var request = new CreateValidatedModelRequest
        {
            Email = new PatchField<string>("user@test.com"),
            Quantity = new PatchField<int>(0),
        };
        var errors = request.Validate();
        Assert.Contains(errors, e => e.Contains("between 1 and 999"));
    }

    [Fact]
    public void Validate_QuantityAboveRange_ReturnsRangeError()
    {
        var request = new CreateValidatedModelRequest
        {
            Email = new PatchField<string>("user@test.com"),
            Quantity = new PatchField<int>(1000),
        };
        var errors = request.Validate();
        Assert.Contains(errors, e => e.Contains("between 1 and 999"));
    }

    [Fact]
    public void Validate_UnsetOptionalFields_NoErrors()
    {
        var request = new CreateValidatedModelRequest
        {
            Email = new PatchField<string>("user@test.com"),
        };
        var errors = request.Validate();
        Assert.DoesNotContain(errors, e => e.Contains("quantity"));
    }
}
