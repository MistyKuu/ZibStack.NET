using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Tests;

// ─── Test models ───────────────────────────────────────────────────

[CreateDto]
[UpdateDto]
public class Article
{
    [DtoIgnore]
    public int Id { get; set; }

    public required string Title { get; set; }
    public string? Body { get; set; }

    [Immutable]
    public required string Slug { get; set; }
}

// ─── Tests ─────────────────────────────────────────────────────────

public class ImmutableTests
{
    [Fact]
    public void Immutable_VisibleInUpdateDto()
    {
        var type = typeof(UpdateArticleRequest);
        Assert.NotNull(type.GetProperty("Slug"));
    }

    [Fact]
    public void Immutable_SkippedInApplyTo()
    {
        var article = new Article { Title = "Old", Slug = "old-slug" };
        var request = new UpdateArticleRequest { Title = "New", Slug = "new-slug" };
        request.ApplyTo(article);

        Assert.Equal("New", article.Title);
        Assert.Equal("old-slug", article.Slug);  // unchanged!
    }

    [Fact]
    public void Immutable_IncludedInCreateToEntity()
    {
        var request = new CreateArticleRequest { Title = "Hello", Slug = "hello" };
        var entity = request.ToEntity();

        Assert.Equal("Hello", entity.Title);
        Assert.Equal("hello", entity.Slug);
    }
}

public class DiffTests
{
    [Fact]
    public void Diff_ReturnsChangedFields()
    {
        var product = new Product
        {
            Name = "Widget",
            Price = 10m,
            Stock = 5,
            Sku = "W-001"
        };

        var request = new UpdateProductRequest { Price = 15m, Stock = 5 };
        var diff = request.Diff(product);

        Assert.Contains("price", diff);
        Assert.DoesNotContain("stock", diff);  // same value
        Assert.DoesNotContain("name", diff);   // not sent
    }

    [Fact]
    public void Diff_EmptyWhenNoChanges()
    {
        var product = new Product { Name = "X", Price = 10m, Sku = "S" };
        var request = new UpdateProductRequest { Price = 10m };
        var diff = request.Diff(product);

        Assert.Empty(diff);
    }
}

public class ToEntityInitOnlyTests
{
    [Fact]
    public void ToEntity_AllPropsInInitializer()
    {
        var request = new CreateProductRequest
        {
            Name = "Widget",
            Sku = "W-001",
            Price = 9.99m,
            Stock = 10,
            Description = "A widget"
        };

        var entity = request.ToEntity();

        Assert.Equal("Widget", entity.Name);
        Assert.Equal("W-001", entity.Sku);
        Assert.Equal(9.99m, entity.Price);
        Assert.Equal(10, entity.Stock);
        Assert.Equal("A widget", entity.Description);
    }

    [Fact]
    public void ToEntity_DefaultsWhenNotSet()
    {
        var request = new CreateProductRequest { Name = "X", Sku = "Y" };
        var entity = request.ToEntity();

        Assert.Equal(0m, entity.Price);
        Assert.Equal(0, entity.Stock);
        Assert.Null(entity.Description);
    }
}

