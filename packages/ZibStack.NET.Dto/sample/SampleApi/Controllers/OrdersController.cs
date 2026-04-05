using Microsoft.AspNetCore.Mvc;
using ZibStack.NET.Dto.Sample.Models;

namespace ZibStack.NET.Dto.Sample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private static readonly List<ExternalOrder> _orders = new()
    {
        new ExternalOrder
        {
            Id = 1,
            ProductName = "Widget",
            Price = 9.99m,
            Quantity = 10,
            Notes = "First order"
        }
    };

    [HttpGet("{id}")]
    public IActionResult Get(int id)
    {
        var order = _orders.FirstOrDefault(o => o.Id == id);
        if (order is null) return NotFound();
        return Ok(order);
    }

    [HttpPost]
    public IActionResult Create([FromBody] CreateOrderRequest request)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
            return BadRequest(new { errors });

        var order = request.ToEntity();
        order.Id = _orders.Count + 1;
        _orders.Add(order);

        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    [HttpPatch("{id}")]
    public IActionResult Update(int id, [FromBody] UpdateOrderRequest request)
    {
        var order = _orders.FirstOrDefault(o => o.Id == id);
        if (order is null) return NotFound();

        var errors = request.Validate();
        if (errors.Count > 0)
            return BadRequest(new { errors });

        request.ApplyTo(order);
        return Ok(order);
    }
}
