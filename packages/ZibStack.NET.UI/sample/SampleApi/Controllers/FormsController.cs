using Microsoft.AspNetCore.Mvc;
using SampleApi.Models;

namespace SampleApi.Controllers;

[ApiController]
[Route("api")]
public class FormsController : ControllerBase
{
    [HttpGet("forms/player")]
    public IActionResult GetPlayerForm()
        => Content(Player.GetFormSchemaJson(), "application/json");

    [HttpGet("tables/player")]
    public IActionResult GetPlayerTable()
        => Content(Player.GetTableSchemaJson(), "application/json");
}
