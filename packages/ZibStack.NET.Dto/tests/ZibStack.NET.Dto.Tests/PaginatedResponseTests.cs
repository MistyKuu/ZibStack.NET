using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Tests;

public class PaginatedResponseTests
{
    [Fact]
    public void Create_SetsAllProperties()
    {
        var items = new[] { "a", "b", "c" };
        var page = PaginatedResponse<string>.Create(items, totalCount: 10, page: 2, pageSize: 3);

        Assert.Equal(items, page.Items);
        Assert.Equal(10, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(3, page.PageSize);
    }

    [Fact]
    public void TotalPages_CalculatesCorrectly()
    {
        var page = PaginatedResponse<int>.Create(new[] { 1 }, totalCount: 10, page: 1, pageSize: 3);

        Assert.Equal(4, page.TotalPages); // ceil(10/3) = 4
    }

    [Fact]
    public void TotalPages_ExactDivision()
    {
        var page = PaginatedResponse<int>.Create(new[] { 1 }, totalCount: 9, page: 1, pageSize: 3);

        Assert.Equal(3, page.TotalPages);
    }

    [Fact]
    public void TotalPages_ZeroPageSize_ReturnsZero()
    {
        var page = PaginatedResponse<int>.Create(Array.Empty<int>(), totalCount: 0, page: 1, pageSize: 0);

        Assert.Equal(0, page.TotalPages);
    }

    [Fact]
    public void HasNextPage_True_WhenNotLastPage()
    {
        var page = PaginatedResponse<int>.Create(new[] { 1, 2, 3 }, totalCount: 10, page: 2, pageSize: 3);

        Assert.True(page.HasNextPage); // page 2 of 4
    }

    [Fact]
    public void HasNextPage_False_WhenLastPage()
    {
        var page = PaginatedResponse<int>.Create(new[] { 10 }, totalCount: 10, page: 4, pageSize: 3);

        Assert.False(page.HasNextPage); // page 4 of 4
    }

    [Fact]
    public void HasPreviousPage_True_WhenNotFirstPage()
    {
        var page = PaginatedResponse<int>.Create(new[] { 1 }, totalCount: 10, page: 2, pageSize: 3);

        Assert.True(page.HasPreviousPage);
    }

    [Fact]
    public void HasPreviousPage_False_WhenFirstPage()
    {
        var page = PaginatedResponse<int>.Create(new[] { 1 }, totalCount: 10, page: 1, pageSize: 3);

        Assert.False(page.HasPreviousPage);
    }

    [Fact]
    public void Map_TransformsItems_PreservesPagination()
    {
        var page = PaginatedResponse<int>.Create(new[] { 1, 2, 3 }, totalCount: 10, page: 2, pageSize: 3);

        var mapped = page.Map(x => x.ToString());

        Assert.Equal(new[] { "1", "2", "3" }, mapped.Items);
        Assert.Equal(10, mapped.TotalCount);
        Assert.Equal(2, mapped.Page);
        Assert.Equal(3, mapped.PageSize);
    }

    [Fact]
    public void CreateAsync_PaginatesQueryable()
    {
        var source = Enumerable.Range(1, 20).AsQueryable();

        var page = PaginatedResponse<int>.CreateAsync(source, page: 2, pageSize: 5).Result;

        Assert.Equal(new[] { 6, 7, 8, 9, 10 }, page.Items);
        Assert.Equal(20, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(5, page.PageSize);
        Assert.Equal(4, page.TotalPages);
    }

    [Fact]
    public void Empty_Page()
    {
        var page = PaginatedResponse<string>.Create(
            Array.Empty<string>(), totalCount: 0, page: 1, pageSize: 10);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalPages);
        Assert.False(page.HasNextPage);
        Assert.False(page.HasPreviousPage);
    }
}
