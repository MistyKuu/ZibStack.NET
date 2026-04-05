using Microsoft.AspNetCore.Mvc;
using ZibStack.NET.Dto.Sample.Models;

namespace ZibStack.NET.Dto.Sample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlayersController : ControllerBase
{
    private static readonly List<Player> _players = new()
    {
        new Player
        {
            Id = 1,
            Name = "Alice",
            Level = 10,
            Email = "alice@example.com",
            Address = new Address { Street = "123 Main St", City = "Springfield", ZipCode = "62701" },
            Password = "hashed_secret",
            IsAdmin = false,
            CreatedAt = DateTime.UtcNow
        }
    };

    [HttpGet("{id}")]
    public IActionResult Get(int id)
    {
        var player = _players.FirstOrDefault(p => p.Id == id);
        if (player is null) return NotFound();
        return Ok(player);
    }

    [HttpPost]
    public IActionResult Create([FromBody] CreatePlayerRequest request)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
            return BadRequest(new { errors });

        var player = request.ToEntity();
        player.Id = _players.Count + 1;
        player.CreatedAt = DateTime.UtcNow;
        _players.Add(player);

        return CreatedAtAction(nameof(Get), new { id = player.Id }, player);
    }

    [HttpPatch("{id}")]
    public IActionResult Update(int id, [FromBody] UpdatePlayerRequest request)
    {
        var player = _players.FirstOrDefault(p => p.Id == id);
        if (player is null) return NotFound();

        var errors = request.Validate();
        if (errors.Count > 0)
            return BadRequest(new { errors });

        request.ApplyTo(player);
        return Ok(player);
    }
}
