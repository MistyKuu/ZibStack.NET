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
dotnet add package ZibStack.NET.Core           # relationship + utility-type attributes
dotnet add package ZibStack.NET.Query          # filter/sort DSL — auto-detected by Dto
dotnet add package ZibStack.NET.Validation
dotnet add package ZibStack.NET.EntityFramework
dotnet add package ZibStack.NET.Log            # [Log] + structured logging interceptors
dotnet add package ZibStack.NET.Aop            # [Trace] + custom aspects
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
dotnet add package Microsoft.AspNetCore.OpenApi
dotnet add package Scalar.AspNetCore
```

> Every ZibStack package is optional — you can start with just `ZibStack.NET.Dto` + `EntityFramework` and grow the dependency set as you reach for each feature. The list above installs everything used in this guide.

## 2. Define your models

```csharp
// Models/Player.cs
using ZibStack.NET.Core;
using ZibStack.NET.Dto;
using ZibStack.NET.Validation;

[CrudApi]
[ZValidate]
public partial class Player
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }

    [ZRequired] [ZMinLength(2)] [ZMaxLength(50)]
    public required string Name { get; set; }

    [ZRange(1, 100)]
    public int Level { get; set; }

    [ZEmail]
    public string? Email { get; set; }

    // Foreign key + navigation property. [OneToOne] tells the Dto generator
    // to expose Team.* as dot-notation filter fields (e.g. filter=Team.Name=Warriors).
    public int? TeamId { get; set; }

    [OneToOne]
    public Team? Team { get; set; }

    [DtoIgnore] public DateTime CreatedAt { get; set; }
}

// Models/Team.cs
using ZibStack.NET.Core;
using ZibStack.NET.Dto;
using ZibStack.NET.Validation;

[CrudApi]
[ZValidate]
public partial class Team
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }

    [ZRequired] [ZMinLength(2)] [ZMaxLength(50)]
    public required string Name { get; set; }

    public string? Description { get; set; }
    public int MaxMembers { get; set; }

    // Inverse side of Player.Team. [OneToMany] enables collection filters like
    // filter=Players.Count>5 or filter=Players.Name=*ski ("any player named *ski").
    [OneToMany]
    public ICollection<Player> Players { get; set; } = new List<Player>();

    [DtoIgnore] public DateTime CreatedAt { get; set; }
}
```

`[CrudApi]` alone generates:
- `CreatePlayerRequest` and `UpdatePlayerRequest` (with `PatchField<T>` for partial updates)
- `PlayerResponse` (for GET responses)
- `PlayerQuery` (nullable filter fields + `ApplyFilter` / `ApplySort`; when `ZibStack.NET.Query` is referenced, also `Apply(filter, sort)` for the DSL)
- Full Minimal API endpoints: `GET /api/players`, `GET /api/players/{id}`, `POST`, `PATCH`, `DELETE`
- The same set for `Team`.

`[OneToOne]` / `[OneToMany]` are markers from `ZibStack.NET.Core` that tell the generators "this property is a navigation, expand it". They're what makes `filter=Team.Name=Warriors` or `filter=Players.Count>5` possible — without them the relation is invisible to the query DSL. See [Core → Relationship Attributes](/ZibStack.NET/packages/core/#relationship-attributes) for the full contract.

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
using ZibStack.NET.Aop;
using ZibStack.NET.Dto;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new PatchFieldJsonConverterFactory()));

builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite("Data Source=app.db"));

builder.Services.AddAppDbContextCrudStores();

// Built-in aspect handlers ([Trace], …). Required if you decorate any
// method/class with [Trace]. [Log] resolves ILogger<T> from DI on its own.
builder.Services.AddAop();

var app = builder.Build();

// Auto-create database (use EF migrations in production)
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

// Bridge DI into the aspect runtime — one call, once at startup.
app.Services.UseAop();

app.MapOpenApi();
app.MapScalarApiReference();

app.MapPlayerEndpoints();
app.MapTeamEndpoints();

app.Run();
```

> **OpenAPI note.** ZibStack.NET.Dto auto-detects `Swashbuckle.AspNetCore` and emits a schema filter so `PatchField<T>` renders as `T | null | omitted` in the Swagger UI. If you stick with `Microsoft.AspNetCore.OpenApi` + Scalar (as in this guide), `PatchField<T>` still works but shows up as an opaque object in the schema. Add `dotnet add package Swashbuckle.AspNetCore` if you want nicer schemas out of the box.

## 5. Seed some data (optional)

The rest of the guide is easier to follow with a few pre-inserted rows. Add a `HasData` call in `OnModelCreating`:

```csharp
// AppDbContext.cs
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Team>().HasData(
        new Team { Id = 1, Name = "Warriors",  MaxMembers = 10, Description = "Golden state" },
        new Team { Id = 2, Name = "Lakers",    MaxMembers = 12, Description = "Purple & gold" },
        new Team { Id = 3, Name = "Knicks",    MaxMembers = 10, Description = "New York" });

    modelBuilder.Entity<Player>().HasData(
        new Player { Id = 1, Name = "Alice",   Level = 55, Email = "alice@test.com", TeamId = 1 },
        new Player { Id = 2, Name = "Bob",     Level = 32, Email = "bob@test.com",   TeamId = 1 },
        new Player { Id = 3, Name = "Kowalski",Level = 78, Email = "k@test.com",     TeamId = 2 },
        new Player { Id = 4, Name = "Diana",   Level = 12, Email = null,             TeamId = 3 });
}
```

Delete `app.db` and restart so `EnsureCreated()` re-seeds.

## 6. Run and test

```bash
dotnet run
```

Open `http://localhost:5000/scalar` for the interactive API docs (Scalar UI).

### Create a player

```bash
curl -X POST http://localhost:5000/api/players \
  -H "Content-Type: application/json" \
  -d '{"name":"Eve","level":10,"email":"eve@test.com","teamId":2}'
```

```json
{
  "id": 5,
  "name": "Eve",
  "level": 10,
  "email": "eve@test.com",
  "teamId": 2,
  "createdAt": "0001-01-01T00:00:00"
}
```

### List players (paginated)

```bash
curl 'http://localhost:5000/api/players?page=1&pageSize=10'
```

```json
{
  "items": [ /* ... */ ],
  "totalCount": 5,
  "page": 1,
  "pageSize": 10,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

### Query DSL — filter, sort, relations

Because `ZibStack.NET.Query` is referenced, every list endpoint accepts `filter` and `sort` query-string parameters parsed as a DSL. Both work across relation boundaries thanks to `[OneToOne]` / `[OneToMany]`:

```bash
# Simple comparison — players above level 30, sorted by level descending
curl 'http://localhost:5000/api/players?filter=Level>30&sort=-Level'

# Multiple filters (comma = AND), case-insensitive contains on name
curl 'http://localhost:5000/api/players?filter=Level>20,Name=*a/i'

# OR group: either low level OR high level, no mid
curl 'http://localhost:5000/api/players?filter=(Level<15|Level>50)'

# IN list — pick by a set of names
curl 'http://localhost:5000/api/players?filter=Name=in=Alice;Bob;Diana'

# Dot notation across [OneToOne] — find Warriors players
curl 'http://localhost:5000/api/players?filter=Team.Name=Warriors'

# Dot notation with EndsWith + multi-field sort
curl 'http://localhost:5000/api/players?filter=Team.Name$s&sort=-Level,Name'

# Collection filter via [OneToMany] — teams with more than 1 player
curl 'http://localhost:5000/api/teams?filter=Players.Count>1'

# Collection filter — teams that contain at least one player matching the predicate
curl 'http://localhost:5000/api/teams?filter=Players.Level>70'

# Field projection — response only carries requested columns
curl 'http://localhost:5000/api/players?select=Name,Level,Team.Name'

# Count only — cheap row count without fetching data
curl 'http://localhost:5000/api/players?filter=Level>20&count=true'
```

Operators: `=` `!=` `>` `>=` `<` `<=` `=*` (contains) `!*` `^` (starts with) `!^` `$` (ends with) `!$` `=in=` `=out=`. Logic: `,` (AND), `|` (OR), `()` (grouping), trailing `/i` for case insensitive.

> **Security.** The field allowlist is **generated at compile time** from your DTO — any `filter=Password=*` or `filter=SomeInternalColumn=...` that isn't on the generated DTO is rejected as an unknown field. No reflection, no runtime column lookup, no way to leak columns you didn't expose.

### PatchField — the null-vs-missing tri-state

PATCH requests use `PatchField<T>` under the hood, which distinguishes **three** states for every field:

1. **Not set** — key missing from the JSON body → field is left untouched
2. **Set to `null`** — key present with value `null` → field is cleared
3. **Set to a value** — key present with a real value → field is written

This is the difference between JSON Merge Patch (RFC 7396) done right and the usual "DTO with nullable properties" pattern that silently loses information.

```bash
# State 1 — not set: only level changes, email stays "alice@test.com"
curl -X PATCH http://localhost:5000/api/players/1 \
  -H "Content-Type: application/json" \
  -d '{"level":99}'

curl http://localhost:5000/api/players/1
# → { "id": 1, "name": "Alice", "level": 99, "email": "alice@test.com", ... }
```

```bash
# State 2 — set to null: explicitly clear the email
curl -X PATCH http://localhost:5000/api/players/1 \
  -H "Content-Type: application/json" \
  -d '{"email":null}'

curl http://localhost:5000/api/players/1
# → { "id": 1, "name": "Alice", "level": 99, "email": null, ... }
```

```bash
# State 3 — set to a value: write a new email
curl -X PATCH http://localhost:5000/api/players/1 \
  -H "Content-Type: application/json" \
  -d '{"email":"alice@newcompany.com"}'

curl http://localhost:5000/api/players/1
# → { "id": 1, "name": "Alice", "level": 99, "email": "alice@newcompany.com", ... }
```

All three look like "a PATCH that touches the email field" if you only look at the request/response types. `PatchField<T>` is what lets the framework tell them apart — see [PatchField Tri-State guide](/ZibStack.NET/guides/patchfield-tri-state/) for the in-depth walkthrough.

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

## 7. Observability — `[Log]` + `[Trace]`

Right now your API runs blind. Let's fix that by adding structured logging and OpenTelemetry spans without changing a single line of business logic.

Add attributes to the store (or to any service you want observed):

```csharp
// PlayerStore.cs — wraps the generated base store
using ZibStack.NET.Aop;
using ZibStack.NET.Log;

[Log]     // automatic entry/exit/exception logs on every public method
[Trace]   // Activity span for every public method (OpenTelemetry-compatible)
public partial class PlayerStore : EfCrudStore<Player, int, AppDbContext>
{
    public PlayerStore(AppDbContext db) : base(db) { }
    protected override DbSet<Player> Set => Db.Players;
}
```

That's it. Every `Create`, `Update`, `Delete`, `GetByIdAsync`, `ListAsync` call now logs:

```
info: PlayerStore[1] Entering PlayerStore.GetByIdAsync(id: 1)
info: PlayerStore[2] Exited PlayerStore.GetByIdAsync in 3ms -> { "Id": 1, "Name": "Alice", ... }
```

…and creates a `System.Diagnostics.Activity` span tagged with `code.namespace`, `code.function`, `elapsed_ms`, parameter values, and status. Any OpenTelemetry exporter (Jaeger, Zipkin, OTLP, Application Insights) wired to an `ActivitySource` listener picks them up automatically.

**Interpolated-string structured logging** — anywhere inside a method, standard `ILogger` calls become structured as long as `ZibStack.NET.Log` is imported:

```csharp
using ZibStack.NET.Log;

app.MapGet("/api/players/{id}", async (int id, ILogger<Program> logger, ICrudStore<Player, int> store) =>
{
    logger.LogInformation($"Looking up player {id}");
    // Template captured as "Looking up player {id}"
    // Structured property: id=42 — Serilog / Seq / Elastic index on it
    var player = await store.GetByIdAsync(id);
    return player is null ? Results.NotFound() : Results.Ok(player);
});
```

No more `logger.LogInformation("Looking up player {Id}", id)` boilerplate — the source generator rewrites the interpolated call into a cached `LoggerMessage.Define<int>` at compile time, so structured properties survive **and** the call is ~40× faster when the log level is disabled.

> ZibStack.NET.Log is **quiet by default** — no forced global usings, no warnings on your existing logging call sites. To opt into strict mode (global using + warnings telling you to migrate legacy calls), set `<ZibLogStrict>true</ZibLogStrict>` in the `.csproj`. See the [Log Configuration reference](/ZibStack.NET/packages/log/#configuration) for the full list of toggles.

For the full end-to-end story (OTLP exporter setup, `[Sensitive]` masking in spans, custom aspects) see the [Observability guide](/ZibStack.NET/guides/observability/).

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

### Tune the default query behavior

`[CrudApi]` implies a `[QueryDto]` for list endpoints. Add an explicit one when you want to override the defaults:

```csharp
[CrudApi]
[QueryDto(DefaultSort = "Name")]        // Sortable is true by default — no extra flag needed
public class Player { ... }
```

Set `Sortable = false` only for endpoints with a fixed result order (analytics, ordered exports) where clients should not be able to re-sort. See [`[QueryDto]` in the Dto reference](/ZibStack.NET/packages/dto/#query--filter-dto-querydto) for all knobs.

### Different response for list vs detail

```csharp
[CrudApi]
public class Player
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Level { get; set; }

    [DtoIgnore(DtoTarget.List)]
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

## Where next

- **[Modeling Relationships & Query DSL](/ZibStack.NET/guides/relationships-query-dsl/)** — deeper walk through `[OneToOne]` / `[OneToMany]` + the full filter DSL, including collection predicates and how it translates to SQL under EF Core.
- **[Declarative Observability](/ZibStack.NET/guides/observability/)** — end-to-end setup for `[Log]` + `[Trace]` + OpenTelemetry exporters (Jaeger, Seq, App Insights), plus `[Sensitive]` for PII.
- **[PatchField Tri-State](/ZibStack.NET/guides/patchfield-tri-state/)** — the null-vs-missing problem in JSON Merge Patch and how `PatchField<T>` solves it, with handler-side pattern matching.
