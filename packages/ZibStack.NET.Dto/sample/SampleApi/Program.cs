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

app.Run();
