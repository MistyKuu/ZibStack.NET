using Xunit;
using ZibStack.NET.Query;

namespace ZibStack.NET.Query.Tests;

public class SortParserTests
{
    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        Assert.Empty(SortParser.Parse(null));
    }

    [Fact]
    public void Parse_Empty_ReturnsEmpty()
    {
        Assert.Empty(SortParser.Parse(""));
    }

    [Fact]
    public void Parse_SimpleAscending()
    {
        var result = SortParser.Parse("Name");
        var clause = Assert.Single(result);
        Assert.Equal("Name", clause.Field);
        Assert.False(clause.Descending);
    }

    [Fact]
    public void Parse_DashPrefix_Descending()
    {
        var result = SortParser.Parse("-Level");
        var clause = Assert.Single(result);
        Assert.Equal("Level", clause.Field);
        Assert.True(clause.Descending);
    }

    [Fact]
    public void Parse_DescKeyword()
    {
        var result = SortParser.Parse("Level desc");
        var clause = Assert.Single(result);
        Assert.Equal("Level", clause.Field);
        Assert.True(clause.Descending);
    }

    [Fact]
    public void Parse_AscKeyword()
    {
        var result = SortParser.Parse("Name asc");
        var clause = Assert.Single(result);
        Assert.Equal("Name", clause.Field);
        Assert.False(clause.Descending);
    }

    [Fact]
    public void Parse_MultipleFields()
    {
        var result = SortParser.Parse("-Level,Name");
        Assert.Equal(2, result.Count);
        Assert.Equal("Level", result[0].Field);
        Assert.True(result[0].Descending);
        Assert.Equal("Name", result[1].Field);
        Assert.False(result[1].Descending);
    }

    [Fact]
    public void Parse_DotNotation()
    {
        var result = SortParser.Parse("Team.Name");
        var clause = Assert.Single(result);
        Assert.Equal("Team.Name", clause.Field);
        Assert.False(clause.Descending);
    }

    [Fact]
    public void Parse_DotNotation_Descending()
    {
        var result = SortParser.Parse("-Team.Name");
        var clause = Assert.Single(result);
        Assert.Equal("Team.Name", clause.Field);
        Assert.True(clause.Descending);
    }
}
