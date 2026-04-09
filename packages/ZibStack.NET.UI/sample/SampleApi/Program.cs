using Scalar.AspNetCore;
using SampleApi;
using SampleApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseStaticFiles();
app.MapOpenApi();
app.MapScalarApiReference();

// CRUD endpoints NOT mapped — this is a read-only playground demo.
// In a real app you'd add:
//   builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite("Data Source=app.db"));
//   builder.Services.AddAppDbContextCrudStores();
//   app.MapProductEndpoints();

// Form/table schemas (generated from [ImTiredOfCrud] on Product)
app.MapGet("/api/forms/product", () =>
    Results.Content(Product.GetFormSchemaJson(), "application/json"));
app.MapGet("/api/tables/product", () =>
    Results.Content(Product.GetTableSchemaJson(), "application/json"));

// Live playground: POST C# code → compile with generators → return schemas
app.MapPlayground();

app.Run();
