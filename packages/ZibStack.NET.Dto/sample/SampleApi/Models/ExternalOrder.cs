namespace ZibStack.NET.Dto.Sample.Models;

// Simulates an external class you don't control
public class ExternalOrder
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string? Notes { get; set; }
}
