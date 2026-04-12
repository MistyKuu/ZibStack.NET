using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Tests;

// ─── Test models ───────────────────────────────────────────────────

[CreateDto]
[UpdateDto]
public class Article
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }

    public required string Title { get; set; }
    public string? Body { get; set; }

    [DtoIgnore(DtoTarget.Update)]
    public required string Slug { get; set; }
}

// ─── Tests ─────────────────────────────────────────────────────────

public class DtoIgnoreUpdateTests
{
    [Fact]
    public void DtoIgnoreUpdate_ExcludedFromUpdateDto()
    {
        var type = typeof(UpdateArticleRequest);
        Assert.Null(type.GetProperty("Slug"));
    }

    [Fact]
    public void DtoIgnoreUpdate_IncludedInCreateDto()
    {
        var type = typeof(CreateArticleRequest);
        Assert.NotNull(type.GetProperty("Slug"));
    }

    [Fact]
    public void DtoIgnoreUpdate_IncludedInCreateToEntity()
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

public class DtoMapperTests
{
    [Fact]
    public void Map_CreatesNewInstance()
    {
        var source = new Product
        {
            Id = 1,
            Name = "Widget",
            Price = 9.99m,
            Stock = 10,
            Sku = "W-001"
        };

        var target = DtoMapper.Map<Product, ProductCopy>(source);

        Assert.Equal("Widget", target.Name);
        Assert.Equal(9.99m, target.Price);
        Assert.Equal(10, target.Stock);
    }

    [Fact]
    public void MapTo_CopiesMatchingProps()
    {
        var source = new Product { Name = "A", Price = 5m, Sku = "S" };
        var target = new ProductCopy();

        DtoMapper.MapTo(source, target);

        Assert.Equal("A", target.Name);
        Assert.Equal(5m, target.Price);
    }

    [Fact]
    public void Map_IgnoresNonMatchingProps()
    {
        var source = new Product { Name = "A", Sku = "S" };
        var target = DtoMapper.Map<Product, ProductCopy>(source);

        Assert.Equal(0, target.ExtraField);  // not on source
    }

    public class ProductCopy
    {
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public int ExtraField { get; set; }
    }
}
