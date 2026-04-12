using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Tests;

[QueryDto]
public class Inventory
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }

    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public bool InStock { get; set; }
}

public class QueryDtoTests
{
    [Fact]
    public void QueryDto_AllPropertiesNullable()
    {
        var type = typeof(InventoryQuery);
        Assert.True(Nullable.GetUnderlyingType(type.GetProperty("Price")!.PropertyType) == typeof(decimal));
        Assert.True(Nullable.GetUnderlyingType(type.GetProperty("Stock")!.PropertyType) == typeof(int));
        Assert.True(Nullable.GetUnderlyingType(type.GetProperty("InStock")!.PropertyType) == typeof(bool));
        // string is already nullable reference type
        Assert.Equal(typeof(string), type.GetProperty("Name")!.PropertyType);
    }

    [Fact]
    public void QueryDto_ExcludesIgnoredProperties()
    {
        Assert.Null(typeof(InventoryQuery).GetProperty("Id"));
    }

    [Fact]
    public void QueryDto_HasApplyFilter()
    {
        var method = typeof(InventoryQuery).GetMethod("ApplyFilter");
        Assert.NotNull(method);
    }

    [Fact]
    public void QueryDto_ApplyFilter_FiltersMatching()
    {
        var items = new List<Inventory>
        {
            new() { Id = 1, Name = "Widget", Price = 10m, Stock = 5, InStock = true },
            new() { Id = 2, Name = "Gadget", Price = 20m, Stock = 0, InStock = false },
            new() { Id = 3, Name = "Widget", Price = 15m, Stock = 3, InStock = true }
        }.AsQueryable();

        var query = new InventoryQuery { Name = "Widget" };
        var result = query.ApplyFilter(items).ToList();

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal("Widget", r.Name));
    }

    [Fact]
    public void QueryDto_ApplyFilter_EmptyQuery_NoFilter()
    {
        var items = new List<Inventory>
        {
            new() { Id = 1, Name = "A", Price = 1m, Stock = 1, InStock = true },
            new() { Id = 2, Name = "B", Price = 2m, Stock = 2, InStock = false }
        }.AsQueryable();

        var query = new InventoryQuery();
        var result = query.ApplyFilter(items).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void QueryDto_ApplyFilter_MultipleFilters()
    {
        var items = new List<Inventory>
        {
            new() { Id = 1, Name = "Widget", Price = 10m, Stock = 5, InStock = true },
            new() { Id = 2, Name = "Widget", Price = 20m, Stock = 0, InStock = false },
        }.AsQueryable();

        var query = new InventoryQuery { Name = "Widget", InStock = true };
        var result = query.ApplyFilter(items).ToList();

        Assert.Single(result);
        Assert.Equal(10m, result[0].Price);
    }
}
