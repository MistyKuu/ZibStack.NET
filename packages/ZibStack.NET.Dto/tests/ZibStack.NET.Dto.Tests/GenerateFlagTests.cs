namespace ZibStack.NET.Dto.Tests;

public class GenerateFlagTests
{
    [Fact]
    public void CreateOnly_GeneratesCreateRequest()
    {
        Assert.NotNull(typeof(CreateCreateOnlyModelRequest));
    }

    [Fact]
    public void CreateOnly_DoesNotGenerateUpdateRequest()
    {
        var type = typeof(CreateCreateOnlyModelRequest).Assembly
            .GetType("ZibStack.NET.Dto.Tests.UpdateCreateOnlyModelRequest");
        Assert.Null(type);
    }

    [Fact]
    public void CreateOnly_HasValidateAndToEntity()
    {
        var type = typeof(CreateCreateOnlyModelRequest);
        Assert.NotNull(type.GetMethod("Validate"));
        Assert.NotNull(type.GetMethod("ToEntity"));
    }

    [Fact]
    public void UpdateOnly_GeneratesUpdateRequest()
    {
        Assert.NotNull(typeof(UpdateUpdateOnlyModelRequest));
    }

    [Fact]
    public void UpdateOnly_DoesNotGenerateCreateRequest()
    {
        var type = typeof(UpdateUpdateOnlyModelRequest).Assembly
            .GetType("ZibStack.NET.Dto.Tests.CreateUpdateOnlyModelRequest");
        Assert.Null(type);
    }

    [Fact]
    public void UpdateOnly_HasValidateAndApplyTo()
    {
        var type = typeof(UpdateUpdateOnlyModelRequest);
        Assert.NotNull(type.GetMethod("Validate"));
        Assert.NotNull(type.GetMethod("ApplyTo"));
    }

    [Fact]
    public void BothAttributes_GeneratesBothRequests()
    {
        // Product has both [CreateDto] and [UpdateDto]
        Assert.NotNull(typeof(CreateProductRequest));
        Assert.NotNull(typeof(UpdateProductRequest));
    }
}
