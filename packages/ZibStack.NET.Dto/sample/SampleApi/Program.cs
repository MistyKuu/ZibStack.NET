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

// ─── [Destructurable<T>] demo ───────────────────────────────────────
// Shape-record approach: a user-declared partial record carries the picked
// property list; the generator fills in `Rest`, `FromSource`, `RestOf` and
// `Split` — giving you typed `{ picked, ...rest }` with IDE support on both
// sides.
app.MapGet("/destructure", () =>
{
    var person = new Person("Alice", 42, "alice@example.com", 30, "Warsaw");

    // Pick Name only → rest has Id, Email, Age, City
    var (just, rest1) = PersonJustName.Split(person);

    // Pick Name + Id → rest has Email, Age, City
    var (pair, rest2) = PersonNameId.Split(person);

    // Body-style shape (no primary ctor) also works — object-initializer path
    var (contact, rest3) = PersonContact.Split(person);

    return Results.Ok(new
    {
        single = new { just.Name, rest = new { rest1.Id, rest1.Email, rest1.Age, rest1.City } },
        pair = new { pair.Name, pair.Id, rest = new { rest2.Email, rest2.Age, rest2.City } },
        contact = new { contact.Name, contact.Email, rest = new { rest3.Id, rest3.Age, rest3.City } }
    });
});

app.Run();
