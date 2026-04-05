using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Tests;

public class GenericTests
{
    [Fact]
    public void Generic_CreateRequest_Exists()
    {
        var type = typeof(CreateWrapperRequest<>);
        Assert.True(type.IsGenericTypeDefinition);
    }

    [Fact]
    public void Generic_UpdateRequest_Exists()
    {
        var type = typeof(UpdateWrapperRequest<>);
        Assert.True(type.IsGenericTypeDefinition);
    }

    [Fact]
    public void Generic_CreateRequest_HasTypedProperty()
    {
        var closedType = typeof(CreateWrapperRequest<int>);
        var valueProp = closedType.GetProperty("Value")!;
        Assert.Equal(typeof(PatchField<int>), valueProp.PropertyType);
    }

    [Fact]
    public void Generic_CreateRequest_ToEntity()
    {
        var request = new CreateWrapperRequest<string> { Value = "hello", Label = "test" };
        var entity = request.ToEntity();

        Assert.IsType<Wrapper<string>>(entity);
        Assert.Equal("hello", entity.Value);
        Assert.Equal("test", entity.Label);
    }

    [Fact]
    public void Generic_UpdateRequest_ApplyTo()
    {
        var wrapper = new Wrapper<int> { Value = 1, Label = "old" };
        var request = new UpdateWrapperRequest<int> { Label = "new" };
        request.ApplyTo(wrapper);

        Assert.Equal(1, wrapper.Value);  // unchanged
        Assert.Equal("new", wrapper.Label);
    }

    [Fact]
    public void Generic_Implements_Interfaces()
    {
        Assert.True(typeof(ICanCreate<Wrapper<int>>).IsAssignableFrom(typeof(CreateWrapperRequest<int>)));
        Assert.True(typeof(ICanApply<Wrapper<int>>).IsAssignableFrom(typeof(UpdateWrapperRequest<int>)));
    }
}
