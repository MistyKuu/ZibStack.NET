namespace ZibStack.NET.Dto.Tests;

public class ApplyWithChangesTests
{
    [Fact]
    public void ApplyWithChanges_ReturnsChangedFields()
    {
        var product = new Product
        {
            Name = "Widget",
            Price = 10m,
            Stock = 5,
            Sku = "W-001"
        };

        var request = new UpdateProductRequest { Price = 15m, Stock = 5 };
        var (changed, entity) = request.ApplyWithChanges(product);

        Assert.Contains("price", changed);
        Assert.DoesNotContain("stock", changed);  // same value — applied but not "changed"
        Assert.Equal(15m, entity.Price);  // applied
        Assert.Equal(5, entity.Stock);     // applied (same value)
    }

    [Fact]
    public void ApplyWithChanges_EmptyRequest_NoChanges()
    {
        var product = new Product { Name = "X", Price = 10m, Sku = "S" };
        var request = new UpdateProductRequest();
        var (changed, _) = request.ApplyWithChanges(product);

        Assert.Empty(changed);
    }

    [Fact]
    public void ApplyWithChanges_MutatesEntity()
    {
        var product = new Product { Name = "Old", Price = 5m, Sku = "S" };
        var request = new UpdateProductRequest { Name = "New" };
        var (_, entity) = request.ApplyWithChanges(product);

        Assert.Same(product, entity);
        Assert.Equal("New", product.Name);
    }

    [Fact]
    public void ApplyWithChanges_SetOnceFieldNotInUpdateDto()
    {
        // Slug has [DtoIgnore(DtoTarget.Update)] — it's not in UpdateArticleRequest at all
        var type = typeof(UpdateArticleRequest);
        Assert.Null(type.GetProperty("Slug"));

        // ApplyWithChanges only touches fields that exist on the DTO
        var article = new Article { Title = "Old", Slug = "old-slug" };
        var request = new UpdateArticleRequest { Title = "New" };
        var (changed, _) = request.ApplyWithChanges(article);

        Assert.Contains("title", changed);
        Assert.Equal("old-slug", article.Slug);  // untouched — field not in update DTO
    }
}
