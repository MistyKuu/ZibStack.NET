using System.Text.Json;
using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Tests;

public class PatchFieldTests
{
    [Fact]
    public void Default_HasValue_IsFalse()
    {
        var field = new PatchField<string>();
        Assert.False(field.HasValue);
    }

    [Fact]
    public void WithValue_HasValue_IsTrue()
    {
        var field = new PatchField<string>("hello");
        Assert.True(field.HasValue);
        Assert.Equal("hello", field.Value);
    }

    [Fact]
    public void WithNull_HasValue_IsTrue()
    {
        var field = new PatchField<string?>(null);
        Assert.True(field.HasValue);
        Assert.Null(field.Value);
    }

    [Fact]
    public void ImplicitConversion_FromValue()
    {
        PatchField<int> field = 42;
        Assert.True(field.HasValue);
        Assert.Equal(42, field.Value);
    }

    [Fact]
    public void ImplicitConversion_ToValue()
    {
        var field = new PatchField<string>("test");
        string value = field;
        Assert.Equal("test", value);
    }

    [Fact]
    public void ToString_WithValue()
    {
        var field = new PatchField<int>(5);
        Assert.Equal("Value(5)", field.ToString());
    }

    [Fact]
    public void ToString_WithoutValue()
    {
        var field = new PatchField<int>();
        Assert.Equal("Undefined", field.ToString());
    }

    [Fact]
    public void JsonDeserialization_PresentField_HasValue()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new PatchFieldJsonConverterFactory());

        var json = """{"Name":"Bob"}""";
        var result = JsonSerializer.Deserialize<TestDto>(json, options);

        Assert.NotNull(result);
        Assert.True(result!.Name.HasValue);
        Assert.Equal("Bob", result.Name.Value);
        Assert.False(result.Age.HasValue);
    }

    [Fact]
    public void JsonDeserialization_NullField_HasValueTrue()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new PatchFieldJsonConverterFactory());

        var json = """{"Name":null}""";
        var result = JsonSerializer.Deserialize<TestDto>(json, options);

        Assert.NotNull(result);
        Assert.True(result!.Name.HasValue);
        Assert.Null(result.Name.Value);
    }

    [Fact]
    public void JsonDeserialization_MissingField_HasValueFalse()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new PatchFieldJsonConverterFactory());

        var json = """{"Age":25}""";
        var result = JsonSerializer.Deserialize<TestDto>(json, options);

        Assert.NotNull(result);
        Assert.False(result!.Name.HasValue);
        Assert.True(result.Age.HasValue);
        Assert.Equal(25, result.Age.Value);
    }

    private class TestDto
    {
        public PatchField<string?> Name { get; set; }
        public PatchField<int> Age { get; set; }
    }
}
