using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Tests.Sortable;

[QueryDto(Sortable = true, DefaultSort = "Name")]
public class Product
{
    [DtoIgnore]
    public int Id { get; set; }

    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int Stock { get; set; }
}

public class SortableQueryTests
{
    private static readonly List<Product> Products = new()
    {
        new() { Id = 1, Name = "Cherry", Price = 3m, Stock = 10 },
        new() { Id = 2, Name = "Apple", Price = 1m, Stock = 30 },
        new() { Id = 3, Name = "Banana", Price = 2m, Stock = 20 },
    };

    [Fact]
    public void SortableQuery_HasSortByProperty()
    {
        var prop = typeof(ProductQuery).GetProperty("SortBy");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop.PropertyType);
    }

    [Fact]
    public void SortableQuery_HasSortDirectionProperty()
    {
        var prop = typeof(ProductQuery).GetProperty("SortDirection");
        Assert.NotNull(prop);
    }

    [Fact]
    public void SortableQuery_HasApplySortMethod()
    {
        Assert.NotNull(typeof(ProductQuery).GetMethod("ApplySort"));
    }

    [Fact]
    public void SortableQuery_HasApplyMethod()
    {
        Assert.NotNull(typeof(ProductQuery).GetMethod("Apply"));
    }

    [Fact]
    public void ApplySort_ByName_Asc()
    {
        var query = new ProductQuery { SortBy = "Name", SortDirection = SortDirection.Asc };
        var result = query.ApplySort(Products.AsQueryable()).ToList();

        Assert.Equal("Apple", result[0].Name);
        Assert.Equal("Banana", result[1].Name);
        Assert.Equal("Cherry", result[2].Name);
    }

    [Fact]
    public void ApplySort_ByPrice_Desc()
    {
        var query = new ProductQuery { SortBy = "Price", SortDirection = SortDirection.Desc };
        var result = query.ApplySort(Products.AsQueryable()).ToList();

        Assert.Equal(3m, result[0].Price);
        Assert.Equal(2m, result[1].Price);
        Assert.Equal(1m, result[2].Price);
    }

    [Fact]
    public void ApplySort_CaseInsensitive()
    {
        var query = new ProductQuery { SortBy = "pRiCe", SortDirection = SortDirection.Asc };
        var result = query.ApplySort(Products.AsQueryable()).ToList();

        Assert.Equal(1m, result[0].Price);
    }

    [Fact]
    public void ApplySort_DefaultSort_UsedWhenNoSortBy()
    {
        var query = new ProductQuery(); // No SortBy → defaults to "Name"
        var result = query.ApplySort(Products.AsQueryable()).ToList();

        Assert.Equal("Apple", result[0].Name);
        Assert.Equal("Banana", result[1].Name);
        Assert.Equal("Cherry", result[2].Name);
    }

    [Fact]
    public void ApplySort_UnknownField_ReturnsUnchanged()
    {
        var query = new ProductQuery { SortBy = "NonExistent" };
        var result = query.ApplySort(Products.AsQueryable()).ToList();

        Assert.Equal(3, result.Count); // no crash, returns original order
    }

    [Fact]
    public void Apply_FiltersAndSorts()
    {
        var query = new ProductQuery
        {
            Stock = 20,
            SortBy = "Name",
            SortDirection = SortDirection.Asc
        };

        var result = query.Apply(Products.AsQueryable()).ToList();

        Assert.Single(result);
        Assert.Equal("Banana", result[0].Name);
    }

    [Fact]
    public void ApplyFilter_StillWorks()
    {
        var query = new ProductQuery { Name = "Apple" };
        var result = query.ApplyFilter(Products.AsQueryable()).ToList();

        Assert.Single(result);
        Assert.Equal("Apple", result[0].Name);
    }
}
