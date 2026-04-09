using Xunit;
using ZibStack.NET.Query;

namespace ZibStack.NET.Query.Tests;

public class SelectParserTests
{
    [Fact]
    public void Parse_Null_ReturnsEmpty() => Assert.Empty(SelectParser.Parse(null));

    [Fact]
    public void Parse_SingleField()
    {
        var result = SelectParser.Parse("Name");
        Assert.Single(result);
        Assert.Contains("name", result);
    }

    [Fact]
    public void Parse_MultipleFields()
    {
        var result = SelectParser.Parse("Name,Level,Email");
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Parse_CaseInsensitive()
    {
        var result = SelectParser.Parse("NAME,level");
        Assert.Contains("name", result);
        Assert.Contains("level", result);
    }

    [Fact]
    public void Parse_DotNotation()
    {
        var result = SelectParser.Parse("Name,Team.Name,Team.City");
        Assert.Contains("team.name", result);
        Assert.Contains("team.city", result);
    }

    [Fact]
    public void GetNavigationPrefixes()
    {
        var fields = SelectParser.Parse("Name,Team.Name,Team.City,Category.Id");
        var prefixes = SelectParser.GetNavigationPrefixes(fields);
        Assert.Equal(2, prefixes.Count);
        Assert.Contains("team", prefixes);
        Assert.Contains("category", prefixes);
    }
}
