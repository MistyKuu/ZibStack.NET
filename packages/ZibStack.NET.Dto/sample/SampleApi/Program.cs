using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using ZibStack.NET.Dto;
using ZibStack.NET.Dto.Sample;
using ZibStack.NET.Dto.Sample.Models;

var builder = WebApplication.CreateBuilder(args);

// JSON converters for PatchField
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new PatchFieldJsonConverterFactory()));

// OpenAPI + Scalar
builder.Services.AddOpenApi();

// EF Core + SQLite
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite("Data Source=sample.db"));

// Auto-generated CRUD stores (with audit timestamps)
builder.Services.AddAppDbContextCrudStores();

var app = builder.Build();

// Auto-create database
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

// API docs
app.MapOpenApi();
app.MapScalarApiReference();

// Auto-generated CRUD endpoints
app.MapPlayerEndpoints();
app.MapTeamEndpoints();

// ─── [Destructurable] demo ──────────────────────────────────────────
// JS-style destructuring: var (name, rest) = person.PickName()
// Generator scans PickXxx() call sites and emits typed picks on demand.
app.MapGet("/destructure", () =>
{
    var person = new Person
    {
        Name = "Alice",
        Id = 42,
        Email = "alice@example.com",
        Age = 30,
        City = "Warsaw"
    };

    // Single property pick — rest contains everything else
    var (name, rest1) = person.PickName();

    // Multi-property pick — pick Name + Id, rest contains Email/Age/City
    var (name2, id, rest2) = person.PickNameId();

    // Three-property pick — pick Name + Id + Email, rest contains Age/City
    var (name3, id3, email, rest3) = person.PickNameIdEmail();

    return Results.Ok(new
    {
        single = new { name, rest = new { rest1.Id, rest1.Email, rest1.Age, rest1.City } },
        pair = new { name = name2, id, rest = new { rest2.Email, rest2.Age, rest2.City } },
        triple = new { name = name3, id = id3, email, rest = new { rest3.Age, rest3.City } }
    });
});

app.Run();
