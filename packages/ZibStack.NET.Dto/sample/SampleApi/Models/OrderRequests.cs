using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Sample.Models;

[CreateDtoFor(typeof(ExternalOrder), Ignore = new[] { "Id" }, Required = new[] { "ProductName" })]
public partial record CreateOrderRequest;

[UpdateDtoFor(typeof(ExternalOrder), Ignore = new[] { "Id" })]
public partial record UpdateOrderRequest;
