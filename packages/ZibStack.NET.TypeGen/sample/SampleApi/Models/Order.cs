using ZibStack.NET.TypeGen;

namespace SampleApi.Models;

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi,
               OutputDir = "generated")]
public class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }

    [TsName("creditCardLast4")]
    [OpenApiProperty(Format = "password", Description = "Last four digits only.")]
    public string? CreditCardMasked { get; set; }

    [TsIgnore]
    [OpenApiIgnore]
    public string InternalAuditId { get; set; } = "";

    public List<OrderItem> Items { get; set; } = new();
}

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi,
               OutputDir = "generated")]
public class OrderItem
{
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi,
               OutputDir = "generated")]
public enum OrderStatus
{
    Pending = 0,
    Shipped = 1,
    Delivered = 2,
    Cancelled = 3,
}
