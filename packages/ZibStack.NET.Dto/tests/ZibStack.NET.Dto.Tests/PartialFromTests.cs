namespace ZibStack.NET.Dto.Tests;

public class PartialFromTests
{
    [Fact]
    public void PartialFrom_IsRecord()
    {
        Assert.True(typeof(PartialProduct).IsClass);
        // Records have a special <Clone>$ method
        Assert.NotNull(typeof(PartialProduct).GetMethod("<Clone>$"));
    }

    [Fact]
    public void PartialFrom_IncludesAllProperties()
    {
        var type = typeof(PartialProduct);
        Assert.NotNull(type.GetProperty("Id"));
        Assert.NotNull(type.GetProperty("Name"));
        Assert.NotNull(type.GetProperty("Price"));
        Assert.NotNull(type.GetProperty("Description"));
        Assert.NotNull(type.GetProperty("Stock"));
        Assert.NotNull(type.GetProperty("Sku"));
        Assert.NotNull(type.GetProperty("DiscontinuedReason"));
        Assert.NotNull(type.GetProperty("CreatedAt"));
    }

    [Fact]
    public void PartialFrom_IncludesIgnoredProperties()
    {
        // PartialFrom includes ALL properties, even [DtoIgnore] ones
        var type = typeof(PartialProduct);
        Assert.NotNull(type.GetProperty("Id"));
        Assert.NotNull(type.GetProperty("CreatedAt"));
    }

    [Fact]
    public void PartialFrom_NoValidateMethod()
    {
        var type = typeof(PartialProduct);
        Assert.Null(type.GetMethod("Validate"));
        Assert.Null(type.GetMethod("ValidateForCreate"));
        Assert.Null(type.GetMethod("ValidateForUpdate"));
    }

    [Fact]
    public void PartialFrom_NoToEntityMethod()
    {
        var type = typeof(PartialProduct);
        Assert.Null(type.GetMethod("ToEntity"));
    }

    [Fact]
    public void PartialFrom_HasApplyTo()
    {
        var type = typeof(PartialProduct);
        var method = type.GetMethod("ApplyTo");
        Assert.NotNull(method);
        Assert.Equal(typeof(Product), method!.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void PartialFrom_ApplyTo_OnlySetsProvided()
    {
        var product = new Product
        {
            Id = 1,
            Name = "Original",
            Price = 10m,
            Stock = 5,
            Sku = "SKU-001",
            CreatedAt = new DateTime(2020, 1, 1)
        };

        var partial = new PartialProduct { Price = 15m, Stock = 20 };
        partial.ApplyTo(product);

        Assert.Equal(1, product.Id);              // unchanged
        Assert.Equal("Original", product.Name);   // unchanged
        Assert.Equal(15m, product.Price);          // updated
        Assert.Equal(20, product.Stock);           // updated
        Assert.Equal("SKU-001", product.Sku);     // unchanged
    }

    [Fact]
    public void PartialFrom_AllFieldsUndefined_NoChanges()
    {
        var product = new Product
        {
            Id = 1,
            Name = "Widget",
            Price = 9.99m,
            Sku = "WDG-001"
        };

        var partial = new PartialProduct();
        partial.ApplyTo(product);

        Assert.Equal("Widget", product.Name);
        Assert.Equal(9.99m, product.Price);
    }
}
