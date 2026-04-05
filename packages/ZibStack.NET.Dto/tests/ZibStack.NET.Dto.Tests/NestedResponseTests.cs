using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Tests;

[ResponseDto]
public class Order
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public OrderLine? Line { get; set; }
}

[ResponseDto]
public class OrderLine
{
    public string Product { get; set; } = "";
    public int Qty { get; set; }
}

public class NestedResponseTests
{
    [Fact]
    public void Nested_PropertyType_IsResponseDto()
    {
        var prop = typeof(OrderResponse).GetProperty("Line")!;
        Assert.Equal(typeof(OrderLineResponse), Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
    }

    [Fact]
    public void Nested_FromEntity_MapsNestedObject()
    {
        var order = new Order
        {
            Id = 1,
            Title = "Test",
            Line = new OrderLine { Product = "Widget", Qty = 5 }
        };

        var response = OrderResponse.FromEntity(order);

        Assert.Equal(1, response.Id);
        Assert.Equal("Test", response.Title);
        Assert.NotNull(response.Line);
        Assert.Equal("Widget", response.Line!.Product);
        Assert.Equal(5, response.Line.Qty);
    }

    [Fact]
    public void Nested_FromEntity_NullNested_ReturnsNull()
    {
        var order = new Order { Id = 1, Title = "Test", Line = null };

        var response = OrderResponse.FromEntity(order);

        Assert.Null(response.Line);
    }

    [Fact]
    public void Nested_ProjectFrom_SkipsNestedProperties()
    {
        var orders = new List<Order>
        {
            new() { Id = 1, Title = "A" },
            new() { Id = 2, Title = "B" }
        }.AsQueryable();

        var responses = OrderResponse.ProjectFrom(orders).ToList();

        Assert.Equal(2, responses.Count);
        Assert.Equal("A", responses[0].Title);
        // Line is skipped in projection — will be null
        Assert.Null(responses[0].Line);
    }
}
