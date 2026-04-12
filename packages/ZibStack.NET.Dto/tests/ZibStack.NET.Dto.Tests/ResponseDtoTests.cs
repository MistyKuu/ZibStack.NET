namespace ZibStack.NET.Dto.Tests;

public class ResponseDtoTests
{
    [Fact]
    public void ResponseDto_GeneratesRecord()
    {
        Assert.NotNull(typeof(ProductResponse).GetMethod("<Clone>$"));
    }

    [Fact]
    public void ResponseDto_RespectsTargetedIgnore()
    {
        var type = typeof(ProductResponse);
        // [DtoIgnore(DtoTarget.Create | Update | Query)] on Id — keeps it in Response
        Assert.NotNull(type.GetProperty("Id"));
        // [DtoIgnore] (= All) on CreatedAt — excluded from Response too
        Assert.Null(type.GetProperty("CreatedAt"));
    }

    [Fact]
    public void ResponseDto_IncludesAllPublicProperties()
    {
        var type = typeof(ProductResponse);
        Assert.NotNull(type.GetProperty("Name"));
        Assert.NotNull(type.GetProperty("Price"));
        Assert.NotNull(type.GetProperty("Description"));
        Assert.NotNull(type.GetProperty("Stock"));
        Assert.NotNull(type.GetProperty("Sku"));
        Assert.NotNull(type.GetProperty("DiscontinuedReason"));
    }

    [Fact]
    public void ResponseDto_NoPatchField()
    {
        // Response properties are plain types, not PatchField<T>
        var nameProp = typeof(ProductResponse).GetProperty("Name")!;
        Assert.Equal(typeof(string), nameProp.PropertyType);
    }

    [Fact]
    public void ResponseDto_FromEntity_MapsAllProperties()
    {
        var product = new Product
        {
            Id = 42,
            Name = "Widget",
            Price = 9.99m,
            Stock = 10,
            Description = "A widget",
            Sku = "WDG-001",
            DiscontinuedReason = null,
            CreatedAt = new DateTime(2024, 1, 1)
        };

        var response = ProductResponse.FromEntity(product);

        Assert.Equal(42, response.Id);
        Assert.Equal("Widget", response.Name);
        Assert.Equal(9.99m, response.Price);
        Assert.Equal(10, response.Stock);
        Assert.Equal("A widget", response.Description);
        Assert.Equal("WDG-001", response.Sku);
        // CreatedAt has [DtoIgnore] (All) — not in Response
    }

    [Fact]
    public void ResponseDto_ProjectFrom_ReturnsQueryable()
    {
        var products = new List<Product>
        {
            new() { Id = 1, Name = "A", Price = 1m, Sku = "S1" },
            new() { Id = 2, Name = "B", Price = 2m, Sku = "S2" }
        }.AsQueryable();

        var responses = ProductResponse.ProjectFrom(products).ToList();

        Assert.Equal(2, responses.Count);
        Assert.Equal("A", responses[0].Name);
        Assert.Equal("B", responses[1].Name);
    }

    [Fact]
    public void ResponseDto_HasNoValidateMethod()
    {
        var type = typeof(ProductResponse);
        Assert.Null(type.GetMethod("Validate"));
    }

    [Fact]
    public void ResponseDto_HasNoApplyTo()
    {
        var type = typeof(ProductResponse);
        Assert.Null(type.GetMethod("ApplyTo"));
    }

    [Fact]
    public void ResponseDto_HasNoToEntity()
    {
        var type = typeof(ProductResponse);
        Assert.Null(type.GetMethod("ToEntity"));
    }
}
