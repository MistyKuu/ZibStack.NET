using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Tests;

public class RenamePropertyTests
{
    [Fact]
    public void RenameProperty_DtoHasNewName()
    {
        var type = typeof(CreateUserRequest);
        Assert.NotNull(type.GetProperty("Name"));
        Assert.Null(type.GetProperty("FirstName"));
    }

    [Fact]
    public void RenameProperty_ToEntity_MapsBackToOriginal()
    {
        var request = new CreateUserRequest { Name = "Bob", Email = "bob@test.com" };
        var entity = request.ToEntity();

        Assert.IsType<ExternalUser>(entity);
        Assert.Equal("Bob", entity.FirstName);  // mapped back
        Assert.Equal("bob@test.com", entity.Email);
    }

    [Fact]
    public void RenameProperty_ApplyTo_MapsBackToOriginal()
    {
        var user = new ExternalUser { Id = 1, FirstName = "Old", LastName = "X", Email = "old@test.com" };
        var request = new UpdateUserRequest { Name = "New" };
        request.ApplyTo(user);

        Assert.Equal("New", user.FirstName);  // mapped back
        Assert.Equal("old@test.com", user.Email);  // unchanged
    }

    [Fact]
    public void RenameProperty_IgnoredPropsExcluded()
    {
        var type = typeof(CreateUserRequest);
        Assert.Null(type.GetProperty("Id"));
        Assert.Null(type.GetProperty("LastName"));
    }

    [Fact]
    public void RenameProperty_JsonNameUsesDtoName()
    {
        // The JSON name should be camelCase of the new name
        var request = new CreateUserRequest { Name = "Bob", Email = "b@t.com" };
        var errors = request.Validate();
        // "name" not "firstName" in error messages
        Assert.DoesNotContain(errors, e => e.Contains("firstName"));
    }
}
