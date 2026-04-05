namespace ZibStack.NET.Dto.Tests;

public class SeparateModeTests
{
    // ─── CreateRequest ─────────────────────────────────────────────

    [Fact]
    public void Create_Validate_RequiredMissing_ReturnsError()
    {
        var request = new CreateProductRequest();
        var errors = request.Validate();

        Assert.Contains(errors, e => e.Contains("name") && e.Contains("required"));
        Assert.Contains(errors, e => e.Contains("sku") && e.Contains("required"));
    }

    [Fact]
    public void Create_Validate_RequiredNull_ReturnsError()
    {
        var request = new CreateProductRequest { Name = null!, Sku = null! };
        var errors = request.Validate();

        Assert.Contains(errors, e => e.Contains("name") && e.Contains("null"));
        Assert.Contains(errors, e => e.Contains("sku") && e.Contains("null"));
    }

    [Fact]
    public void Create_Validate_RequiredProvided_NoErrors()
    {
        var request = new CreateProductRequest { Name = "Widget", Sku = "WDG-001" };
        var errors = request.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void Create_DoesNotContain_UpdateOnlyProperty()
    {
        var type = typeof(CreateProductRequest);
        Assert.Null(type.GetProperty("DiscontinuedReason"));
    }

    [Fact]
    public void Create_Contains_CreateOnlyProperty()
    {
        var type = typeof(CreateProductRequest);
        Assert.NotNull(type.GetProperty("Sku"));
    }

    [Fact]
    public void Create_DoesNotContain_IgnoredProperty()
    {
        var type = typeof(CreateProductRequest);
        Assert.Null(type.GetProperty("Id"));
        Assert.Null(type.GetProperty("CreatedAt"));
    }

    [Fact]
    public void Create_ToEntity_SetsRequiredFields()
    {
        var request = new CreateProductRequest
        {
            Name = "Widget",
            Sku = "WDG-001",
            Price = 9.99m
        };

        var entity = request.ToEntity();

        Assert.Equal("Widget", entity.Name);
        Assert.Equal("WDG-001", entity.Sku);
        Assert.Equal(9.99m, entity.Price);
    }

    [Fact]
    public void Create_ToEntity_OptionalNotSet_UsesDefault()
    {
        var request = new CreateProductRequest { Name = "Widget", Sku = "WDG-001" };
        var entity = request.ToEntity();

        Assert.Equal(0, entity.Stock);
        Assert.Null(entity.Description);
    }

    // ─── UpdateRequest ─────────────────────────────────────────────

    [Fact]
    public void Update_Validate_Empty_NoErrors()
    {
        var request = new UpdateProductRequest();
        var errors = request.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void Update_Validate_NonNullableSetToNull_ReturnsError()
    {
        var request = new UpdateProductRequest { Name = null! };
        var errors = request.Validate();
        Assert.Contains(errors, e => e.Contains("name") && e.Contains("null"));
    }

    [Fact]
    public void Update_Validate_NullableSetToNull_NoError()
    {
        var request = new UpdateProductRequest { Description = null };
        var errors = request.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void Update_DoesNotContain_CreateOnlyProperty()
    {
        var type = typeof(UpdateProductRequest);
        Assert.Null(type.GetProperty("Sku"));
    }

    [Fact]
    public void Update_Contains_UpdateOnlyProperty()
    {
        var type = typeof(UpdateProductRequest);
        Assert.NotNull(type.GetProperty("DiscontinuedReason"));
    }

    [Fact]
    public void Update_ApplyTo_OnlySetsProvidedFields()
    {
        var product = new Product
        {
            Id = 1,
            Name = "Old",
            Price = 5.00m,
            Stock = 10,
            Sku = "OLD-001",
            CreatedAt = DateTime.UtcNow
        };

        var request = new UpdateProductRequest { Price = 7.50m };
        request.ApplyTo(product);

        Assert.Equal("Old", product.Name);  // unchanged
        Assert.Equal(7.50m, product.Price); // updated
        Assert.Equal(10, product.Stock);     // unchanged
    }

    [Fact]
    public void Update_ApplyTo_CanSetNullableToNull()
    {
        var product = new Product
        {
            Name = "Widget",
            Sku = "WDG-001",
            Description = "Some description"
        };

        var request = new UpdateProductRequest { Description = null };
        request.ApplyTo(product);

        Assert.Null(product.Description);
    }

    // ─── All optional model ────────────────────────────────────────

    [Fact]
    public void Create_AllOptional_Validate_NoErrors()
    {
        var request = new CreateSettingsRequest();
        var errors = request.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void Create_AllOptional_ToEntity_Defaults()
    {
        var request = new CreateSettingsRequest();
        var entity = request.ToEntity();

        Assert.Null(entity.Theme);
        Assert.Equal(0, entity.FontSize);
        Assert.False(entity.DarkMode);
    }
}
