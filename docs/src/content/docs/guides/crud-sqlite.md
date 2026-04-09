---
title: Full CRUD API with SQLite
description: Step-by-step guide to building a complete REST API with EF Core and SQLite using ZibStack.NET source generators.
---

This guide walks you through building a fully functional CRUD REST API backed by SQLite — from zero to working endpoints in under 5 minutes.

> **See the working sample:** [SampleApi on GitHub](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Dto/sample/SampleApi)

## Prerequisites

- .NET 10 SDK (or 8+)
- Any IDE (Rider, VS Code, Visual Studio)

## 1. Create the project

```bash
dotnet new web -n MyCrudApi
cd MyCrudApi
dotnet add package ZibStack.NET.Dto
dotnet add package ZibStack.NET.Validation
dotnet add package ZibStack.NET.EntityFramework
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
dotnet add package Microsoft.AspNetCore.OpenApi
dotnet add package Scalar.AspNetCore
```

## 2. Define your models

```csharp
// Models/Player.cs
using ZibStack.NET.Dto;
using ZibStack.NET.Validation;

[CrudApi]
[ZValidate]
public partial class Player
{
    [DtoIgnore] public int Id { get; set; }

    [ZRequired] [ZMinLength(2)] [ZMaxLength(50)]
    public required string Name { get; set; }

    [ZRange(1, 100)]
    public int Level { get; set; }

    [ZEmail]
    public string? Email { get; set; }

    [DtoIgnore] public DateTime CreatedAt { get; set; }
}

[CrudApi]
public class Team
{
    [DtoIgnore] public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int MaxMembers { get; set; }
    [DtoIgnore] public DateTime CreatedAt { get; set; }
}
```

`[CrudApi]` alone generates:
- `CreatePlayerRequest` and `UpdatePlayerRequest` (with PatchField for partial updates)
- `PlayerResponse` (for GET responses)
- Full Minimal API endpoints (GET, POST, PATCH, DELETE)

## 3. Set up the database

```csharp
// AppDbContext.cs
using Microsoft.EntityFrameworkCore;
using ZibStack.NET.EntityFramework;

[GenerateCrudStores]
public class AppDbContext : DbContext
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Team> Teams => Set<Team>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}
```

`[GenerateCrudStores]` auto-generates:
- `PlayerEfStore` and `TeamEfStore` (EfCrudStore implementations)
- `AddAppDbContextCrudStores()` extension method for DI registration

## 4. Wire up Program.cs

```csharp
// Program.cs
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using ZibStack.NET.Dto;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new PatchFieldJsonConverterFactory()));

builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite("Data Source=app.db"));

builder.Services.AddAppDbContextCrudStores();

var app = builder.Build();

// Auto-create database
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

app.MapOpenApi();
app.MapScalarApiReference();

app.MapPlayerEndpoints();
app.MapTeamEndpoints();

app.Run();
```

## 5. Run and test

```bash
dotnet run
```

Open `http://localhost:5000/scalar` for the interactive API docs (Scalar UI).

### Create a player

```bash
curl -X POST http://localhost:5000/api/players \
  -H "Content-Type: application/json" \
  -d '{"name":"Alice","level":10,"email":"alice@example.com"}'
```

```json
{
  "id": 1,
  "name": "Alice",
  "level": 10,
  "email": "alice@example.com",
  "createdAt": "0001-01-01T00:00:00"
}
```

### List players (paginated)

```bash
curl http://localhost:5000/api/players?page=1&pageSize=10
```

```json
{
  "items": [{"id": 1, "name": "Alice", "level": 10, "email": "alice@example.com"}],
  "totalCount": 1,
  "page": 1,
  "pageSize": 10,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

### Partial update (only change level)

```bash
curl -X PATCH http://localhost:5000/api/players/1 \
  -H "Content-Type: application/json" \
  -d '{"level":99}'
```

Only `level` is updated — `name` and `email` stay unchanged. This is the power of `PatchField<T>`.

### Validation error

```bash
curl -X POST http://localhost:5000/api/players \
  -H "Content-Type: application/json" \
  -d '{"level":5}'
```

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "name": ["is required."]
  }
}
```

All errors use RFC 9110 ProblemDetails format with per-property error grouping.

### Delete

```bash
curl -X DELETE http://localhost:5000/api/players/1
# 204 No Content
```

## What was generated?

Behind the scenes, the source generators created:

| Generated class | From | Purpose |
|----------------|------|---------|
| `CreatePlayerRequest` | `[CrudApi]` auto-implied `[CreateDto]` | POST request body with validation |
| `UpdatePlayerRequest` | `[CrudApi]` auto-implied `[UpdateDto]` | PATCH request body with PatchField |
| `PlayerResponse` | `[CrudApi]` auto-implied `[ResponseDto]` | GET response shape |
| `PlayerEndpoints` | `[CrudApi]` | Minimal API endpoints (GET, POST, PATCH, DELETE) |
| `PlayerEfStore` | `[GenerateCrudStores]` | EfCrudStore implementation |
| `AppDbContextCrudStoreExtensions` | `[GenerateCrudStores]` | `AddAppDbContextCrudStores()` DI method |

All generated code is in `obj/Debug/net10.0/generated/` — fully inspectable.

## Customizing

### Add filtering and sorting

```csharp
[CrudApi]
[QueryDto(Sortable = true, DefaultSort = "Name")]
public class Player { ... }
```

Now `GET /api/players?name=Ali&sortBy=level&sortDirection=desc` works.

### Different response for list vs detail

```csharp
[CrudApi]
public class Player
{
    [DtoIgnore] public int Id { get; set; }
    public required string Name { get; set; }
    public int Level { get; set; }

    [ListIgnore]
    public string? Bio { get; set; }  // only in GET /api/players/{id}
}
```

### Bulk operations

```csharp
[CrudApi(Operations = CrudOperations.AllWithBulk)]
public class Player { ... }
```

Adds `POST /api/players/bulk` and `POST /api/players/bulk-delete`.

### Per-operation authorization

```csharp
[CrudApi(
    AuthorizePolicy = "read:players",
    CreatePolicy = "write:players",
    DeletePolicy = "admin"
)]
public class Player { ... }
```

### Custom store behavior

```csharp
public class PlayerStore : EfCrudStore<Player, int, AppDbContext>
{
    public PlayerStore(AppDbContext db) : base(db) { }
    protected override DbSet<Player> Set => Db.Players;

    public override async ValueTask CreateAsync(Player entity, CancellationToken ct = default)
    {
        entity.CreatedAt = DateTime.UtcNow;
        await base.CreateAsync(entity, ct);
    }
}

// Register manually instead of AddAppDbContextCrudStores():
builder.Services.AddScoped<ICrudStore<Player, int>, PlayerStore>();
```
