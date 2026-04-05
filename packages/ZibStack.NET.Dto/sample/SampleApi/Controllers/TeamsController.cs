using Microsoft.AspNetCore.Mvc;
using ZibStack.NET.Dto.Sample.Models;
using ZibStack.NET.Dto;

namespace ZibStack.NET.Dto.Sample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TeamsController : ControllerBase
{
    private static readonly List<Team> _teams = new()
    {
        new Team
        {
            Id = 1,
            Name = "Alpha",
            Description = "First team",
            MaxMembers = 5,
            CreatedAt = DateTime.UtcNow
        }
    };

    [HttpGet("{id}")]
    public IActionResult Get(int id)
    {
        var team = _teams.FirstOrDefault(t => t.Id == id);
        if (team is null) return NotFound();
        return Ok(team);
    }

    [HttpPost]
    public IActionResult Create([FromBody] TeamRequest request)
    {
        var errors = request.ValidateForCreate();
        if (errors.Count > 0)
            return BadRequest(new { errors });

        var team = request.ToEntity();
        team.Id = _teams.Count + 1;
        team.CreatedAt = DateTime.UtcNow;
        _teams.Add(team);

        return CreatedAtAction(nameof(Get), new { id = team.Id }, team);
    }

    [HttpPatch("{id}")]
    public IActionResult Update(int id, [FromBody] TeamRequest request)
    {
        var team = _teams.FirstOrDefault(t => t.Id == id);
        if (team is null) return NotFound();

        var errors = request.ValidateForUpdate();
        if (errors.Count > 0)
            return BadRequest(new { errors });

        request.ApplyTo(team);
        return Ok(team);
    }
}
