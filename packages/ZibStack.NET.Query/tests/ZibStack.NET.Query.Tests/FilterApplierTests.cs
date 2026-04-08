using System.Linq.Expressions;
using Xunit;
using ZibStack.NET.Query;

namespace ZibStack.NET.Query.Tests;

// ─── Test model ─────────────────────────────────────────────────────

public class TestPlayer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public string? Email { get; set; }
    public PlayerRole Role { get; set; }
    public decimal Salary { get; set; }
    public int? TeamId { get; set; }
}

public enum PlayerRole { Player, Moderator, Admin }

// ─── Tests ──────────────────────────────────────────────────────────

public class FilterApplierTests
{
    private static IQueryable<TestPlayer> Players => new List<TestPlayer>
    {
        new() { Id = 1, Name = "Jan Kowalski", Level = 42, Email = "jan@test.pl", Role = PlayerRole.Player, Salary = 5000, TeamId = 1 },
        new() { Id = 2, Name = "Anna Nowak", Level = 75, Email = "anna@test.pl", Role = PlayerRole.Admin, Salary = 8000, TeamId = 2 },
        new() { Id = 3, Name = "Piotr Ski", Level = 20, Email = null, Role = PlayerRole.Moderator, Salary = 3000, TeamId = 1 },
        new() { Id = 4, Name = "Kasia Wielka", Level = 55, Email = "kasia@test.pl", Role = PlayerRole.Player, Salary = 6000, TeamId = null },
    }.AsQueryable();

    // ─── Equals ─────────────────────────────────────────────────────

    [Fact]
    public void Apply_StringEquals()
    {
        var clause = new FilterClause("Name", FilterOperator.Equals, "Jan Kowalski");
        var result = FilterApplier.Apply(Players, x => x.Name, clause).ToList();
        Assert.Single(result);
        Assert.Equal("Jan Kowalski", result[0].Name);
    }

    [Fact]
    public void Apply_IntEquals()
    {
        var clause = new FilterClause("Level", FilterOperator.Equals, "42");
        var result = FilterApplier.Apply(Players, x => x.Level, clause).ToList();
        Assert.Single(result);
        Assert.Equal(42, result[0].Level);
    }

    // ─── Comparison ─────────────────────────────────────────────────

    [Fact]
    public void Apply_GreaterThan()
    {
        var clause = new FilterClause("Level", FilterOperator.GreaterThan, "50");
        var result = FilterApplier.Apply(Players, x => x.Level, clause).ToList();
        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.True(p.Level > 50));
    }

    [Fact]
    public void Apply_LessThanOrEqual()
    {
        var clause = new FilterClause("Level", FilterOperator.LessThanOrEqual, "42");
        var result = FilterApplier.Apply(Players, x => x.Level, clause).ToList();
        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.True(p.Level <= 42));
    }

    // ─── String operations ──────────────────────────────────────────

    [Fact]
    public void Apply_Contains()
    {
        var clause = new FilterClause("Name", FilterOperator.Contains, "ski");
        var result = FilterApplier.Apply(Players, x => x.Name, clause).ToList();
        Assert.Single(result); // "Jan Kowalski" (case-sensitive: "Piotr Ski" has uppercase S)
        Assert.Equal("Jan Kowalski", result[0].Name);
    }

    [Fact]
    public void Apply_StartsWith()
    {
        var clause = new FilterClause("Name", FilterOperator.StartsWith, "Jan");
        var result = FilterApplier.Apply(Players, x => x.Name, clause).ToList();
        Assert.Single(result);
        Assert.Equal("Jan Kowalski", result[0].Name);
    }

    [Fact]
    public void Apply_EndsWith()
    {
        var clause = new FilterClause("Name", FilterOperator.EndsWith, "Ski");
        var result = FilterApplier.Apply(Players, x => x.Name, clause).ToList();
        Assert.Single(result);
        Assert.Equal("Piotr Ski", result[0].Name);
    }

    [Fact]
    public void Apply_NotContains()
    {
        var clause = new FilterClause("Name", FilterOperator.NotContains, "ski");
        var result = FilterApplier.Apply(Players, x => x.Name, clause).ToList();
        Assert.Equal(3, result.Count); // Anna, Piotr Ski (no lowercase "ski"), Kasia
    }

    // ─── Case insensitive ───────────────────────────────────────────

    [Fact]
    public void Apply_CaseInsensitive_Contains()
    {
        var clause = new FilterClause("Name", FilterOperator.Contains, "SKI", caseInsensitive: true);
        var result = FilterApplier.Apply(Players, x => x.Name, clause).ToList();
        Assert.Equal(2, result.Count); // Jan Kowalski, Piotr Ski
    }

    [Fact]
    public void Apply_CaseInsensitive_Equals()
    {
        var clause = new FilterClause("Name", FilterOperator.Equals, "jan kowalski", caseInsensitive: true);
        var result = FilterApplier.Apply(Players, x => x.Name, clause).ToList();
        Assert.Single(result);
    }

    // ─── Nullable fields ────────────────────────────────────────────

    [Fact]
    public void Apply_NullableInt_Equals()
    {
        var clause = new FilterClause("TeamId", FilterOperator.Equals, "1");
        var result = FilterApplier.Apply(Players, x => x.TeamId, clause).ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Apply_NullableString_Contains_SkipsNull()
    {
        var clause = new FilterClause("Email", FilterOperator.Contains, "test");
        var result = FilterApplier.Apply(Players, x => x.Email, clause).ToList();
        Assert.Equal(3, result.Count); // Piotr has null email, excluded
    }

    // ─── Enum ───────────────────────────────────────────────────────

    [Fact]
    public void Apply_Enum_Equals()
    {
        var clause = new FilterClause("Role", FilterOperator.Equals, "Admin");
        var result = FilterApplier.Apply(Players, x => x.Role, clause).ToList();
        Assert.Single(result);
        Assert.Equal("Anna Nowak", result[0].Name);
    }

    // ─── Decimal ────────────────────────────────────────────────────

    [Fact]
    public void Apply_Decimal_GreaterThan()
    {
        var clause = new FilterClause("Salary", FilterOperator.GreaterThan, "5000");
        var result = FilterApplier.Apply(Players, x => x.Salary, clause).ToList();
        Assert.Equal(2, result.Count); // Anna 8000, Kasia 6000
    }

    // ─── IN / NOT IN ────────────────────────────────────────────────

    [Fact]
    public void Apply_In_String()
    {
        var clause = new FilterClause("Name", FilterOperator.In, "Jan Kowalski;Anna Nowak");
        var result = FilterApplier.Apply(Players, x => x.Name, clause).ToList();
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Apply_In_Int()
    {
        var clause = new FilterClause("Level", FilterOperator.In, "42;75");
        var result = FilterApplier.Apply(Players, x => x.Level, clause).ToList();
        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Level == 42);
        Assert.Contains(result, p => p.Level == 75);
    }

    [Fact]
    public void Apply_NotIn_Int()
    {
        var clause = new FilterClause("Level", FilterOperator.NotIn, "42;75");
        var result = FilterApplier.Apply(Players, x => x.Level, clause).ToList();
        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.True(p.Level != 42 && p.Level != 75));
    }

    [Fact]
    public void Apply_In_Enum()
    {
        var clause = new FilterClause("Role", FilterOperator.In, "Player;Admin");
        var result = FilterApplier.Apply(Players, x => x.Role, clause).ToList();
        Assert.Equal(3, result.Count); // Jan, Anna, Kasia
    }

    // ─── Invalid value → skip ───────────────────────────────────────

    [Fact]
    public void Apply_InvalidValue_ReturnsOriginalQuery()
    {
        var clause = new FilterClause("Level", FilterOperator.Equals, "notanumber");
        var result = FilterApplier.Apply(Players, x => x.Level, clause).ToList();
        Assert.Equal(4, result.Count); // no filter applied
    }

    // ─── ApplyTree: OR ──────────────────────────────────────────────

    [Fact]
    public void ApplyTree_Or()
    {
        var expr = new FilterOr(
            new FilterLeaf(new FilterClause("Level", FilterOperator.GreaterThan, "60")),
            new FilterLeaf(new FilterClause("Level", FilterOperator.LessThan, "25")));

        var result = FilterApplier.ApplyTree(Players, expr, BuildPredicate).ToList();
        Assert.Equal(2, result.Count); // Anna 75, Piotr 20
    }

    // ─── ApplyTree: AND + OR with grouping ──────────────────────────

    [Fact]
    public void ApplyTree_AndOr_Grouped()
    {
        // (Level>60 | Level<25) AND TeamId=1
        var expr = new FilterAnd(
            new FilterOr(
                new FilterLeaf(new FilterClause("Level", FilterOperator.GreaterThan, "60")),
                new FilterLeaf(new FilterClause("Level", FilterOperator.LessThan, "25"))),
            new FilterLeaf(new FilterClause("TeamId", FilterOperator.Equals, "1")));

        var result = FilterApplier.ApplyTree(Players, expr, BuildPredicate).ToList();
        Assert.Single(result); // only Piotr (Level 20, TeamId 1)
        Assert.Equal("Piotr Ski", result[0].Name);
    }

    // ─── ApplyTree: null expression ─────────────────────────────────

    [Fact]
    public void ApplyTree_NullExpression_ReturnsOriginal()
    {
        var result = FilterApplier.ApplyTree(Players, null, BuildPredicate).ToList();
        Assert.Equal(4, result.Count);
    }

    // ─── End-to-end: parse + apply ──────────────────────────────────

    [Fact]
    public void EndToEnd_ParseAndApply()
    {
        var expr = FilterParser.ParseExpression("Level>30,Name=*ski");
        var result = FilterApplier.ApplyTree(Players, expr, BuildPredicate).ToList();
        Assert.Single(result); // Jan Kowalski: Level 42, name contains "ski"
        Assert.Equal("Jan Kowalski", result[0].Name);
    }

    [Fact]
    public void EndToEnd_OrWithIn()
    {
        var expr = FilterParser.ParseExpression("Level=in=42;20|Role=Admin");
        var result = FilterApplier.ApplyTree(Players, expr, BuildPredicate).ToList();
        Assert.Equal(3, result.Count); // Jan(42), Piotr(20), Anna(Admin)
    }

    // ─── Helper ─────────────────────────────────────────────────────

    private static Expression<Func<TestPlayer, bool>>? BuildPredicate(FilterClause clause)
    {
        return clause.Field.ToLowerInvariant() switch
        {
            "name" => FilterApplier.BuildPredicate<TestPlayer, string>(x => x.Name, clause),
            "level" => FilterApplier.BuildPredicate<TestPlayer, int>(x => x.Level, clause),
            "email" => FilterApplier.BuildPredicate<TestPlayer, string?>(x => x.Email, clause),
            "role" => FilterApplier.BuildPredicate<TestPlayer, PlayerRole>(x => x.Role, clause),
            "salary" => FilterApplier.BuildPredicate<TestPlayer, decimal>(x => x.Salary, clause),
            "teamid" => FilterApplier.BuildPredicate<TestPlayer, int?>(x => x.TeamId, clause),
            _ => null,
        };
    }
}
