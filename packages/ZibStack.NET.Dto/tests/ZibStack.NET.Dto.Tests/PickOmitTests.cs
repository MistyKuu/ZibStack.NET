using ZibStack.NET.Dto;
using ZibStack.NET.Core;

namespace ZibStack.NET.Dto.Tests;

[PickFrom(typeof(Product), nameof(Product.Name), nameof(Product.Price))]
public partial record ProductSummary;

[OmitFrom(typeof(Product), nameof(Product.Id), nameof(Product.CreatedAt), nameof(Product.Sku))]
public partial record ProductWithoutMeta;

public class PickFromTests
{
    [Fact]
    public void PickFrom_OnlyIncludesListedProperties()
    {
        var type = typeof(ProductSummary);
        Assert.NotNull(type.GetProperty("Name"));
        Assert.NotNull(type.GetProperty("Price"));
        Assert.Null(type.GetProperty("Id"));
        Assert.Null(type.GetProperty("Stock"));
        Assert.Null(type.GetProperty("Description"));
    }

    [Fact]
    public void PickFrom_HasApplyTo()
    {
        var method = typeof(ProductSummary).GetMethod("ApplyTo");
        Assert.NotNull(method);
    }

    [Fact]
    public void PickFrom_ApplyTo_OnlySetsPickedFields()
    {
        var product = new Product { Name = "Old", Price = 5m, Stock = 10, Sku = "S" };
        var summary = new ProductSummary { Name = "New", Price = 15m };
        summary.ApplyTo(product);

        Assert.Equal("New", product.Name);
        Assert.Equal(15m, product.Price);
        Assert.Equal(10, product.Stock);  // unchanged
    }
}

public class OmitFromTests
{
    [Fact]
    public void OmitFrom_ExcludesListedProperties()
    {
        var type = typeof(ProductWithoutMeta);
        Assert.Null(type.GetProperty("Id"));
        Assert.Null(type.GetProperty("CreatedAt"));
        Assert.Null(type.GetProperty("Sku"));
    }

    [Fact]
    public void OmitFrom_IncludesRemainingProperties()
    {
        var type = typeof(ProductWithoutMeta);
        Assert.NotNull(type.GetProperty("Name"));
        Assert.NotNull(type.GetProperty("Price"));
        Assert.NotNull(type.GetProperty("Description"));
        Assert.NotNull(type.GetProperty("Stock"));
    }
}
