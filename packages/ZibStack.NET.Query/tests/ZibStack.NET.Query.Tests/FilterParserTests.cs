using Xunit;
using ZibStack.NET.Query;

namespace ZibStack.NET.Query.Tests;

public class FilterParserTests
{
    // ─── Basic parsing ──────────────────────────────────────────────

    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        var result = FilterParser.Parse(null);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_Empty_ReturnsEmpty()
    {
        var result = FilterParser.Parse("");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_Whitespace_ReturnsEmpty()
    {
        var result = FilterParser.Parse("   ");
        Assert.Empty(result);
    }

    // ─── Operators ──────────────────────────────────────────────────

    [Theory]
    [InlineData("Name=Jan", "Name", FilterOperator.Equals, "Jan")]
    [InlineData("Name!=Jan", "Name", FilterOperator.NotEquals, "Jan")]
    [InlineData("Level>30", "Level", FilterOperator.GreaterThan, "30")]
    [InlineData("Level>=30", "Level", FilterOperator.GreaterThanOrEqual, "30")]
    [InlineData("Level<50", "Level", FilterOperator.LessThan, "50")]
    [InlineData("Level<=50", "Level", FilterOperator.LessThanOrEqual, "50")]
    [InlineData("Name=*ski", "Name", FilterOperator.Contains, "ski")]
    [InlineData("Name!*test", "Name", FilterOperator.NotContains, "test")]
    [InlineData("Name^Jan", "Name", FilterOperator.StartsWith, "Jan")]
    [InlineData("Name!^Jan", "Name", FilterOperator.NotStartsWith, "Jan")]
    [InlineData("Name$ski", "Name", FilterOperator.EndsWith, "ski")]
    [InlineData("Name!$ski", "Name", FilterOperator.NotEndsWith, "ski")]
    public void Parse_SingleClause_CorrectOperator(string filter, string field, FilterOperator op, string value)
    {
        var result = FilterParser.Parse(filter);
        var clause = Assert.Single(result);
        Assert.Equal(field, clause.Field);
        Assert.Equal(op, clause.Operator);
        Assert.Equal(value, clause.Value);
        Assert.False(clause.CaseInsensitive);
    }

    // ─── IN / NOT IN ────────────────────────────────────────────────

    [Fact]
    public void Parse_InOperator()
    {
        var result = FilterParser.Parse("Name=in=Jan;Anna;Kasia");
        var clause = Assert.Single(result);
        Assert.Equal("Name", clause.Field);
        Assert.Equal(FilterOperator.In, clause.Operator);
        Assert.Equal("Jan;Anna;Kasia", clause.Value);
    }

    [Fact]
    public void Parse_NotInOperator()
    {
        var result = FilterParser.Parse("Level=out=10;20");
        var clause = Assert.Single(result);
        Assert.Equal("Level", clause.Field);
        Assert.Equal(FilterOperator.NotIn, clause.Operator);
        Assert.Equal("10;20", clause.Value);
    }

    // ─── Case insensitive ───────────────────────────────────────────

    [Fact]
    public void Parse_CaseInsensitiveSuffix()
    {
        var result = FilterParser.Parse("Name=jan/i");
        var clause = Assert.Single(result);
        Assert.Equal("Name", clause.Field);
        Assert.Equal("jan", clause.Value);
        Assert.True(clause.CaseInsensitive);
    }

    // ─── AND (comma) ────────────────────────────────────────────────

    [Fact]
    public void Parse_MultipleClauses_And()
    {
        var result = FilterParser.Parse("Level>20,Level<50,Name=*ski");
        Assert.Equal(3, result.Count);
        Assert.Equal("Level", result[0].Field);
        Assert.Equal(FilterOperator.GreaterThan, result[0].Operator);
        Assert.Equal("Level", result[1].Field);
        Assert.Equal(FilterOperator.LessThan, result[1].Operator);
        Assert.Equal("Name", result[2].Field);
        Assert.Equal(FilterOperator.Contains, result[2].Operator);
    }

    // ─── Dot notation ───────────────────────────────────────────────

    [Fact]
    public void Parse_DotNotation()
    {
        var result = FilterParser.Parse("Team.Name=Lakers");
        var clause = Assert.Single(result);
        Assert.Equal("Team.Name", clause.Field);
        Assert.Equal("Lakers", clause.Value);
    }

    // ─── Escape sequences ───────────────────────────────────────────

    [Fact]
    public void Parse_EscapedComma()
    {
        var result = FilterParser.Parse(@"Name=Hello\,World");
        var clause = Assert.Single(result);
        Assert.Equal("Hello,World", clause.Value);
    }

    // ─── Expression tree: OR ────────────────────────────────────────

    [Fact]
    public void ParseExpression_Or()
    {
        var expr = FilterParser.ParseExpression("Level>50|Level<10");
        var or = Assert.IsType<FilterOr>(expr);
        var left = Assert.IsType<FilterLeaf>(or.Left);
        var right = Assert.IsType<FilterLeaf>(or.Right);
        Assert.Equal(FilterOperator.GreaterThan, left.Clause.Operator);
        Assert.Equal("50", left.Clause.Value);
        Assert.Equal(FilterOperator.LessThan, right.Clause.Operator);
        Assert.Equal("10", right.Clause.Value);
    }

    // ─── Expression tree: Grouping ──────────────────────────────────

    [Fact]
    public void ParseExpression_Grouping_OrThenAnd()
    {
        // (Level>50|Level<10),Name=*ski → AND(OR(>50, <10), Contains ski)
        var expr = FilterParser.ParseExpression("(Level>50|Level<10),Name=*ski");
        var and = Assert.IsType<FilterAnd>(expr);
        var or = Assert.IsType<FilterOr>(and.Left);
        var leaf = Assert.IsType<FilterLeaf>(and.Right);
        Assert.Equal(FilterOperator.GreaterThan, ((FilterLeaf)or.Left).Clause.Operator);
        Assert.Equal(FilterOperator.LessThan, ((FilterLeaf)or.Right).Clause.Operator);
        Assert.Equal(FilterOperator.Contains, leaf.Clause.Operator);
        Assert.Equal("ski", leaf.Clause.Value);
    }

    [Fact]
    public void ParseExpression_Precedence_AndBeforeOr()
    {
        // A,B|C → OR(AND(A,B), C) — AND binds tighter than OR
        var expr = FilterParser.ParseExpression("Level>10,Level<50|Name=Admin");
        var or = Assert.IsType<FilterOr>(expr);
        var and = Assert.IsType<FilterAnd>(or.Left);
        Assert.IsType<FilterLeaf>(and.Left);
        Assert.IsType<FilterLeaf>(and.Right);
        var right = Assert.IsType<FilterLeaf>(or.Right);
        Assert.Equal("Admin", right.Clause.Value);
    }

    // ─── Expression tree: Null/empty ────────────────────────────────

    [Fact]
    public void ParseExpression_Null_ReturnsNull()
    {
        Assert.Null(FilterParser.ParseExpression(null));
    }

    [Fact]
    public void ParseExpression_Empty_ReturnsNull()
    {
        Assert.Null(FilterParser.ParseExpression(""));
    }
}
