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
}
