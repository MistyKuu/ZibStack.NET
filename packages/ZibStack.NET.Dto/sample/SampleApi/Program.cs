using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using ZibStack.NET.Dto;
using ZibStack.NET.Dto.Sample;
using ZibStack.NET.Dto.Sample.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new PatchFieldJsonConverterFactory()));

// PatchField converter for Minimal API endpoints
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new PatchFieldJsonConverterFactory()));

// OpenAPI
builder.Services.AddOpenApi();

// EF Core + SQLite — database file lives next to the project
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite("Data Source=sample.db"));

// Auto-generated CRUD stores for all DbSets in AppDbContext
builder.Services.AddAppDbContextCrudStores();

var app = builder.Build();

// Auto-create/migrate the database on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// OpenAPI doc + Scalar UI
app.MapOpenApi();
app.MapScalarApiReference();

// Generated minimal API endpoints
app.MapPlayerEndpoints();
app.MapTeamEndpoints();

// Legacy hand-written controllers still work
app.MapControllers();
app.Run();
