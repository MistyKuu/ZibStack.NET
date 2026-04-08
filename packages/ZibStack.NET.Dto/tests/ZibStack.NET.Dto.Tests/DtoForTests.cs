using System.Linq;

namespace ZibStack.NET.Dto.Tests;

public class CreateDtoForTests
{
    [Fact]
    public void CreateDtoFor_GeneratesRecord()
    {
        Assert.NotNull(typeof(CreateConfigRequest).GetMethod("<Clone>$"));
    }

    [Fact]
    public void CreateDtoFor_IncludesNonIgnoredProperties()
    {
        var type = typeof(CreateConfigRequest);
        Assert.NotNull(type.GetProperty("Key"));
        Assert.NotNull(type.GetProperty("Value"));
        Assert.NotNull(type.GetProperty("IsSecret"));
    }

    [Fact]
    public void CreateDtoFor_ExcludesIgnoredProperties()
    {
        var type = typeof(CreateConfigRequest);
        Assert.Null(type.GetProperty("Id"));
    }

    [Fact]
    public void CreateDtoFor_Validate_RequiredMissing()
    {
        var request = new CreateConfigRequest();
        var errors = request.Validate();
        Assert.True(errors.Errors.ContainsKey("key"));
        Assert.Contains(errors.Errors["key"], e => e.Contains("required"));
        Assert.True(errors.Errors.ContainsKey("value"));
        Assert.Contains(errors.Errors["value"], e => e.Contains("required"));
    }

    [Fact]
    public void CreateDtoFor_Validate_RequiredPresent_NoErrors()
    {
        var request = new CreateConfigRequest { Key = "app.name", Value = "MyApp" };
        var errors = request.Validate();
        Assert.True(errors.IsValid);
    }

    [Fact]
    public void CreateDtoFor_ToEntity_ReturnsTargetType()
    {
        var request = new CreateConfigRequest { Key = "app.name", Value = "MyApp" };
        var entity = request.ToEntity();

        Assert.IsType<ExternalConfig>(entity);
        Assert.Equal("app.name", entity.Key);
        Assert.Equal("MyApp", entity.Value);
    }

    [Fact]
    public void CreateDtoFor_ToEntity_IgnoredFieldHasDefault()
    {
        var request = new CreateConfigRequest { Key = "k", Value = "v" };
        var entity = request.ToEntity();
        Assert.Equal(0, entity.Id);
    }

    [Fact]
    public void CreateDtoFor_HasNoApplyTo()
    {
        var type = typeof(CreateConfigRequest);
        Assert.Null(type.GetMethod("ApplyTo"));
    }
}

public class UpdateDtoForTests
{
    [Fact]
    public void UpdateDtoFor_GeneratesRecord()
    {
        Assert.NotNull(typeof(UpdateConfigRequest).GetMethod("<Clone>$"));
    }

    [Fact]
    public void UpdateDtoFor_IncludesNonIgnoredProperties()
    {
        var type = typeof(UpdateConfigRequest);
        Assert.NotNull(type.GetProperty("Key"));
        Assert.NotNull(type.GetProperty("Value"));
    }

    [Fact]
    public void UpdateDtoFor_ExcludesIgnoredProperties()
    {
        var type = typeof(UpdateConfigRequest);
        Assert.Null(type.GetProperty("Id"));
        Assert.Null(type.GetProperty("IsSecret"));
    }

    [Fact]
    public void UpdateDtoFor_Validate_Empty_NoErrors()
    {
        var request = new UpdateConfigRequest();
        var errors = request.Validate();
        Assert.True(errors.IsValid);
    }

    [Fact]
    public void UpdateDtoFor_Validate_NonNullableNull_ReturnsError()
    {
        var request = new UpdateConfigRequest { Key = null! };
        var errors = request.Validate();
        Assert.True(errors.Errors.ContainsKey("key"));
        Assert.Contains(errors.Errors["key"], e => e.Contains("null"));
    }

    [Fact]
    public void UpdateDtoFor_ApplyTo_OnlySetsProvided()
    {
        var config = new ExternalConfig { Id = 1, Key = "old", Value = "old", IsSecret = true };

        var request = new UpdateConfigRequest { Value = "new" };
        request.ApplyTo(config);

        Assert.Equal(1, config.Id);
        Assert.Equal("old", config.Key);
        Assert.Equal("new", config.Value);
        Assert.True(config.IsSecret);
    }

    [Fact]
    public void UpdateDtoFor_HasNoToEntity()
    {
        var type = typeof(UpdateConfigRequest);
        Assert.Null(type.GetMethod("ToEntity"));
    }
}
