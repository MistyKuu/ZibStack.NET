using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using ZibStack.NET.Dto;
using SampleApi;
using SampleApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new PatchFieldJsonConverterFactory());
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddOpenApi();

// EF Core + SQLite
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite("Data Source=sample.db"));
builder.Services.AddAppDbContextCrudStores();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

app.UseStaticFiles();
app.MapOpenApi();
app.MapScalarApiReference();

// ─── All from [ImTiredOfCrud] — one model, everything generated ─────

// CRUD API: GET/POST/PATCH/DELETE /api/products (with ?filter=&sort=&select=)
app.MapProductEndpoints();

// Form schema: field types, validation, groups, apiUrl, keyProperty
app.MapGet("/api/forms/product", () =>
    Results.Content(Product.GetFormSchemaJson(), "application/json"));

// Table schema: columns, filterOperators, sortable, apiUrl, keyProperty
app.MapGet("/api/tables/product", () =>
    Results.Content(Product.GetTableSchemaJson(), "application/json"));

// Live playground: POST C# code → compile with generators → return schemas
app.MapPlayground();

app.Run();
