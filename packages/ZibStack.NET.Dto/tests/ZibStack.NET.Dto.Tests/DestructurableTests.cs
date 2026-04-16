using ZibStack.NET.Core;

namespace ZibStack.NET.Dto.Tests;

// ─── Source types ─────────────────────────────────────────────────────
// Plain records — no attributes here. Shapes own the destructuring contract.

public record Person(string Name, int Id, string Email, int Age, string City);

public record Receipt(string Customer, decimal Total, DateTime PlacedAt, string? Note);

// ─── Shapes (primary-ctor style) ──────────────────────────────────────

[Destructurable<Person>]
public partial record PersonNameOnly(string Name);

[Destructurable<Person>]
public partial record PersonNameId(string Name, int Id);

[Destructurable<Person>]
public partial record PersonAll(string Name, int Id, string Email, int Age, string City);

// ─── Shape (body / object-init style) ─────────────────────────────────

[Destructurable<Person>]
public partial record PersonBody
{
    public required string Name { get; init; }
    public required string Email { get; init; }
}

// ─── Shape over an unrelated source — different source, exercise reuse ─

[Destructurable<Receipt>]
public partial record ReceiptSummary(string Customer, decimal Total);

// ─── Tests ────────────────────────────────────────────────────────────

public class DestructurableTests
{
    private static readonly Person Sample = new("Alice", 42, "alice@x.io", 30, "Warsaw");

    [Fact]
    public void Split_PrimaryCtor_PicksAndReturnsTypedRest()
    {
        var (picked, rest) = PersonNameId.Split(Sample);

        Assert.Equal("Alice", picked.Name);
        Assert.Equal(42, picked.Id);

        Assert.Equal("alice@x.io", rest.Email);
        Assert.Equal(30, rest.Age);
        Assert.Equal("Warsaw", rest.City);
    }

    [Fact]
    public void Split_BodyStyle_UsesObjectInitializer()
    {
        var (picked, rest) = PersonBody.Split(Sample);

        Assert.Equal("Alice", picked.Name);
        Assert.Equal("alice@x.io", picked.Email);

        Assert.Equal(42, rest.Id);
        Assert.Equal(30, rest.Age);
        Assert.Equal("Warsaw", rest.City);
    }

    [Fact]
    public void Split_SinglePicked_RestHasEverythingElse()
    {
        var (picked, rest) = PersonNameOnly.Split(Sample);

        Assert.Equal("Alice", picked.Name);
        // Rest is positional — verify order matches source order minus the picked field
        Assert.Equal(42, rest.Id);
        Assert.Equal("alice@x.io", rest.Email);
        Assert.Equal(30, rest.Age);
        Assert.Equal("Warsaw", rest.City);
    }

    [Fact]
    public void Split_AllPicked_RestIsEmptyRecord()
    {
        var (picked, rest) = PersonAll.Split(Sample);

        Assert.Equal("Alice", picked.Name);
        Assert.NotNull(rest);
        // Rest type still emitted (positional record with zero parameters).
        Assert.Empty(typeof(PersonAll.Rest).GetProperties());
    }

    [Fact]
    public void FromSource_ReturnsPickedShape()
    {
        var picked = PersonNameId.FromSource(Sample);
        Assert.Equal("Alice", picked.Name);
        Assert.Equal(42, picked.Id);
    }

    [Fact]
    public void RestOf_ReturnsComplement()
    {
        var rest = PersonNameId.RestOf(Sample);
        Assert.Equal("alice@x.io", rest.Email);
        Assert.Equal(30, rest.Age);
        Assert.Equal("Warsaw", rest.City);
    }

    [Fact]
    public void NestedRest_IsAccessibleAsShapeName_Rest()
    {
        // The rest type lives nested under the shape — `PersonNameId.Rest` — so the
        // ergonomics of typing it explicitly stay short.
        Type rest = typeof(PersonNameId.Rest);
        Assert.Contains(rest.GetProperties(), p => p.Name == "Email");
        Assert.Contains(rest.GetProperties(), p => p.Name == "Age");
        Assert.Contains(rest.GetProperties(), p => p.Name == "City");
        Assert.DoesNotContain(rest.GetProperties(), p => p.Name == "Name");
        Assert.DoesNotContain(rest.GetProperties(), p => p.Name == "Id");
    }

    [Fact]
    public void Split_DifferentSourceType_GeneratesIndependentShape()
    {
        var receipt = new Receipt("Bob", 99.5m, new DateTime(2026, 1, 1), null);
        var (picked, rest) = ReceiptSummary.Split(receipt);

        Assert.Equal("Bob", picked.Customer);
        Assert.Equal(99.5m, picked.Total);

        Assert.Equal(new DateTime(2026, 1, 1), rest.PlacedAt);
        Assert.Null(rest.Note);
    }
}
