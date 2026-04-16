using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ZibStack.NET.Dto;
using ZibStack.NET.TypeGen;

namespace SampleApi.Models;

// [CrudApi] makes the Dto generator emit REST endpoints; TypeGen sees the same
// attribute and contributes the matching `paths:` block to openapi.yaml.
// GetById / GetList / Create / Update / Delete are emitted by default.
[CrudApi]
[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.Zod,
               OutputDir = "generated")]
public partial class Order
{
    public int Id { get; set; }
    
    public required string Customer { get; set; } = "";
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

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi | TypeTarget.Python | TypeTarget.Zod,
               OutputDir = "generated")]
public class OrderItem
{
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi | TypeTarget.Python | TypeTarget.Zod,
               OutputDir = "generated")]
public enum OrderStatus
{
    Pending = 0,
    Shipped = 1,
    Delivered = 2,
    Cancelled = 3,
}

// No per-class rename / OpenAPI attrs here — the rename + schema name come from
// the fluent configurator's b.ForType<Customer>() block in TypeGenConfig.cs.
// Demonstrates "configure without touching source" (useful when the DTO lives
// in a referenced library you can't annotate).
[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi | TypeTarget.Python | TypeTarget.Zod,
               OutputDir = "generated")]
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}
