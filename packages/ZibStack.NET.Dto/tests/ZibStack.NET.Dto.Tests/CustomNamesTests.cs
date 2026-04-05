namespace ZibStack.NET.Dto.Tests;

public class CustomNamesTests
{
    [Fact]
    public void Separate_CustomNames_CreateExists()
    {
        Assert.NotNull(typeof(NewItemDto));
    }

    [Fact]
    public void Separate_CustomNames_UpdateExists()
    {
        Assert.NotNull(typeof(EditItemDto));
    }

    [Fact]
    public void Combined_CustomName_Exists()
    {
        Assert.NotNull(typeof(TagDto));
    }

    [Fact]
    public void Combined_CustomName_HasBothValidations()
    {
        var request = new TagDto();

        var createErrors = request.ValidateForCreate();
        Assert.Contains(createErrors, e => e.Contains("label") && e.Contains("required"));

        var updateErrors = request.ValidateForUpdate();
        Assert.Empty(updateErrors);
    }
}
