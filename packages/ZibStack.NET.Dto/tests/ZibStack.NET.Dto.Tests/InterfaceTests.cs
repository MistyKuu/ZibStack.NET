using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Tests;

public class InterfaceTests
{
    // ─── Separate mode ─────────────────────────────────────────────

    [Fact]
    public void CreateRequest_Implements_ICanCreate()
    {
        Assert.True(typeof(ICanCreate<Product>).IsAssignableFrom(typeof(CreateProductRequest)));
    }

    [Fact]
    public void CreateRequest_Implements_ICanValidate()
    {
        Assert.True(typeof(ICanValidate).IsAssignableFrom(typeof(CreateProductRequest)));
    }

    [Fact]
    public void UpdateRequest_Implements_ICanApply()
    {
        Assert.True(typeof(ICanApply<Product>).IsAssignableFrom(typeof(UpdateProductRequest)));
    }

    [Fact]
    public void UpdateRequest_Implements_ICanValidate()
    {
        Assert.True(typeof(ICanValidate).IsAssignableFrom(typeof(UpdateProductRequest)));
    }

    // ─── Combined mode ─────────────────────────────────────────────

    [Fact]
    public void CombinedRequest_Implements_ICanCreate()
    {
        Assert.True(typeof(ICanCreate<Category>).IsAssignableFrom(typeof(CategoryRequest)));
    }

    [Fact]
    public void CombinedRequest_Implements_ICanApply()
    {
        Assert.True(typeof(ICanApply<Category>).IsAssignableFrom(typeof(CategoryRequest)));
    }

    [Fact]
    public void CombinedRequest_DoesNotImplement_ICanValidate()
    {
        // Combined has ValidateForCreate/ValidateForUpdate instead of Validate
        Assert.False(typeof(ICanValidate).IsAssignableFrom(typeof(CategoryRequest)));
    }

    // ─── CreateDtoFor / UpdateDtoFor ───────────────────────────────

    [Fact]
    public void CreateDtoFor_Implements_ICanCreate()
    {
        Assert.True(typeof(ICanCreate<ExternalConfig>).IsAssignableFrom(typeof(CreateConfigRequest)));
    }

    [Fact]
    public void CreateDtoFor_Implements_ICanValidate()
    {
        Assert.True(typeof(ICanValidate).IsAssignableFrom(typeof(CreateConfigRequest)));
    }

    [Fact]
    public void UpdateDtoFor_Implements_ICanApply()
    {
        Assert.True(typeof(ICanApply<ExternalConfig>).IsAssignableFrom(typeof(UpdateConfigRequest)));
    }

    [Fact]
    public void UpdateDtoFor_Implements_ICanValidate()
    {
        Assert.True(typeof(ICanValidate).IsAssignableFrom(typeof(UpdateConfigRequest)));
    }

    // ─── Generic usage ─────────────────────────────────────────────

    [Fact]
    public void CanUseGenericCreate()
    {
        var request = new CreateProductRequest { Name = "Test", Sku = "T-001" };
        var entity = UseCreate<Product>(request);
        Assert.Equal("Test", entity.Name);
    }

    [Fact]
    public void CanUseGenericApply()
    {
        var product = new Product { Name = "Old", Sku = "OLD" };
        var request = new UpdateProductRequest { Name = "New" };
        UseApply(product, request);
        Assert.Equal("New", product.Name);
    }

    private static T UseCreate<T>(ICanCreate<T> request) => request.ToEntity();
    private static void UseApply<T>(T target, ICanApply<T> request) => request.ApplyTo(target);
}
