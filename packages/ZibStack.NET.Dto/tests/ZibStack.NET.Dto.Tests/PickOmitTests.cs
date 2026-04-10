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
    public void PickFrom_HasFromEntity()
    {
        // Pick is a pure projection — emits a static FromEntity(source) factory,
        // not ApplyTo. ApplyTo would imply partial-update semantics, which is the
        // job of [PartialFrom].
        var method = typeof(ProductSummary).GetMethod("FromEntity", new[] { typeof(Product) });
        Assert.NotNull(method);
        Assert.True(method!.IsStatic);
    }

    [Fact]
    public void PickFrom_FromEntity_CopiesPickedFields()
    {
        var product = new Product { Name = "Widget", Price = 15m, Stock = 10, Sku = "S" };
        var summary = ProductSummary.FromEntity(product);

        Assert.Equal("Widget", summary.Name);
        Assert.Equal(15m, summary.Price);
        // Other fields are not on the projection — Stock/Sku not present.
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
