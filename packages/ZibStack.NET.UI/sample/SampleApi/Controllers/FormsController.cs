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

    // ERP-style views
    [HttpGet("forms/voivodeship")]
    public IActionResult GetVoivodeshipForm()
        => Content(VoivodeshipView.GetFormSchemaJson(), "application/json");

    [HttpGet("tables/voivodeship")]
    public IActionResult GetVoivodeshipTable()
        => Content(VoivodeshipView.GetTableSchemaJson(), "application/json");

    [HttpGet("tables/county")]
    public IActionResult GetCountyTable()
        => Content(CountyView.GetTableSchemaJson(), "application/json");

    [HttpGet("tables/postalcode")]
    public IActionResult GetPostalCodeTable()
        => Content(PostalCodeView.GetTableSchemaJson(), "application/json");
}
