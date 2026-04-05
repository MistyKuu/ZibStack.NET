using System.Text.Json;
using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Tests;

public class JsonSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new PatchFieldJsonConverterFactory() }
    };

    [Fact]
    public void Deserialize_CreateRequest_AllFields()
    {
        var json = """{"name":"Widget","price":9.99,"sku":"WDG-001","stock":10,"description":"A widget"}""";
        var request = JsonSerializer.Deserialize<CreateProductRequest>(json, Options);

        Assert.NotNull(request);
        Assert.True(request!.Name.HasValue);
        Assert.Equal("Widget", request.Name.Value);
        Assert.True(request.Price.HasValue);
        Assert.Equal(9.99m, request.Price.Value);
        Assert.True(request.Sku.HasValue);
        Assert.Equal("WDG-001", request.Sku.Value);
    }

    [Fact]
    public void Deserialize_UpdateRequest_PartialFields()
    {
        var json = """{"price":15.00}""";
        var request = JsonSerializer.Deserialize<UpdateProductRequest>(json, Options);

        Assert.NotNull(request);
        Assert.False(request!.Name.HasValue);
        Assert.True(request.Price.HasValue);
        Assert.Equal(15.00m, request.Price.Value);
        Assert.False(request.Stock.HasValue);
    }

    [Fact]
    public void Deserialize_NullField_HasValueTrueValueNull()
    {
        var json = """{"description":null}""";
        var request = JsonSerializer.Deserialize<UpdateProductRequest>(json, Options);

        Assert.NotNull(request);
        Assert.True(request!.Description.HasValue);
        Assert.Null(request.Description.Value);
    }

    [Fact]
    public void Deserialize_EmptyObject_AllFieldsUndefined()
    {
        var json = "{}";
        var request = JsonSerializer.Deserialize<UpdateProductRequest>(json, Options);

        Assert.NotNull(request);
        Assert.False(request!.Name.HasValue);
        Assert.False(request.Price.HasValue);
        Assert.False(request.Stock.HasValue);
        Assert.False(request.Description.HasValue);
    }

    [Fact]
    public void Deserialize_Combined_Works()
    {
        var json = """{"name":"Electronics","sortOrder":1}""";
        var request = JsonSerializer.Deserialize<CategoryRequest>(json, Options);

        Assert.NotNull(request);
        Assert.True(request!.Name.HasValue);
        Assert.Equal("Electronics", request.Name.Value);
        Assert.True(request.SortOrder.HasValue);
        Assert.Equal(1, request.SortOrder.Value);
        Assert.False(request.Description.HasValue);
    }

    [Fact]
    public void RoundTrip_CreateAndValidateAndConvert()
    {
        var json = """{"name":"Gadget","price":19.99,"sku":"GDG-001"}""";
        var request = JsonSerializer.Deserialize<CreateProductRequest>(json, Options);

        Assert.NotNull(request);
        var errors = request!.Validate();
        Assert.Empty(errors);

        var entity = request.ToEntity();
        Assert.Equal("Gadget", entity.Name);
        Assert.Equal(19.99m, entity.Price);
        Assert.Equal("GDG-001", entity.Sku);
    }

    [Fact]
    public void RoundTrip_UpdateAndValidateAndApply()
    {
        var product = new Product
        {
            Name = "Old",
            Price = 5.00m,
            Sku = "OLD-001",
            Stock = 100
        };

        var json = """{"price":7.50,"stock":50}""";
        var request = JsonSerializer.Deserialize<UpdateProductRequest>(json, Options);

        Assert.NotNull(request);
        var errors = request!.Validate();
        Assert.Empty(errors);

        request.ApplyTo(product);
        Assert.Equal("Old", product.Name);
        Assert.Equal(7.50m, product.Price);
        Assert.Equal(50, product.Stock);
    }
}
