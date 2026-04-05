namespace ZibStack.NET.Dto.Tests;

public class CombinedModeTests
{
    [Fact]
    public void Combined_GeneratesSingleClass()
    {
        Assert.NotNull(typeof(CategoryRequest));
    }

    [Fact]
    public void Combined_ValidateForCreate_RequiredMissing_ReturnsError()
    {
        var request = new CategoryRequest();
        var errors = request.ValidateForCreate();
        Assert.Contains(errors, e => e.Contains("name") && e.Contains("required"));
    }

    [Fact]
    public void Combined_ValidateForCreate_RequiredPresent_NoErrors()
    {
        var request = new CategoryRequest { Name = "Electronics" };
        var errors = request.ValidateForCreate();
        Assert.Empty(errors);
    }

    [Fact]
    public void Combined_ValidateForUpdate_Empty_NoErrors()
    {
        var request = new CategoryRequest();
        var errors = request.ValidateForUpdate();
        Assert.Empty(errors);
    }

    [Fact]
    public void Combined_ValidateForUpdate_NonNullableNull_ReturnsError()
    {
        var request = new CategoryRequest { Name = null! };
        var errors = request.ValidateForUpdate();
        Assert.Contains(errors, e => e.Contains("name") && e.Contains("null"));
    }

    [Fact]
    public void Combined_ToEntity_SetsFields()
    {
        var request = new CategoryRequest
        {
            Name = "Books",
            Description = "All books",
            SortOrder = 3
        };

        var entity = request.ToEntity();

        Assert.Equal("Books", entity.Name);
        Assert.Equal("All books", entity.Description);
        Assert.Equal(3, entity.SortOrder);
    }

    [Fact]
    public void Combined_ApplyTo_OnlySetsProvided()
    {
        var category = new Category
        {
            Id = 1,
            Name = "Old",
            Description = "Old desc",
            SortOrder = 1
        };

        var request = new CategoryRequest { SortOrder = 5 };
        request.ApplyTo(category);

        Assert.Equal("Old", category.Name);
        Assert.Equal("Old desc", category.Description);
        Assert.Equal(5, category.SortOrder);
    }

    [Fact]
    public void Combined_DoesNotContain_IgnoredProperty()
    {
        var type = typeof(CategoryRequest);
        Assert.Null(type.GetProperty("Id"));
    }
}
