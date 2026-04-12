---
title: ZibStack.NET.Dto
description: A C# source generator that produces strongly-typed Create, Update, Response, and Query DTOs from domain models, with optional full CRUD API endpoint generation.
---

[![NuGet](https://img.shields.io/nuget/v/ZibStack.NET.Dto.svg)](https://www.nuget.org/packages/ZibStack.NET.Dto) [![Source](https://img.shields.io/badge/source-GitHub-blue)](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Dto)

A C# source generator that produces strongly-typed **Create**, **Update**, **Response**, and **Query** DTOs from your domain models, and optionally generates **full CRUD API endpoints** (Minimal API + MVC Controllers). No reflection, no runtime overhead. Supports generics, inheritance, nested types, flattening, validation propagation, and more.

> **See the working sample:** [SampleApi on GitHub](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Dto/sample/SampleApi)

## Install

```
dotnet add package ZibStack.NET.Dto
dotnet add package ZibStack.NET.Core
```

`ZibStack.NET.Core` provides TypeScript-style utility types (`[PartialFrom]`, `[IntersectFrom]`, `[PickFrom]`, `[OmitFrom]`).

## Quick start

Mark your model with `[CreateDto]` and/or `[UpdateDto]`:

```csharp
using ZibStack.NET.Dto;

[CreateDto]
[UpdateDto]
public class Player
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }

    public required string Name { get; set; }
    public int Level { get; set; }
    public string? Email { get; set; }
}
```

Register the JSON converter:

```csharp
// Minimal API:
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new PatchFieldJsonConverterFactory()));

// Or MVC Controllers:
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        new PatchFieldJsonConverterFactory()));
```

## Modes

### Separate (`[CreateDto]` + `[UpdateDto]`)

Generates two records -- `CreatePlayerRequest` and `UpdatePlayerRequest`:

```csharp
[HttpPost]
public IActionResult Create([FromBody] CreatePlayerRequest request)
{
    var validation = request.Validate();
    if (!validation.IsValid)
        return BadRequest(new { validation.Errors });

    Player player = request.ToEntity();
    return Ok(player);
}

[HttpPatch("{id}")]
public IActionResult Update(int id, [FromBody] UpdatePlayerRequest request)
{
    var validation = request.Validate();
    if (!validation.IsValid)
        return BadRequest(new { validation.Errors });

    request.ApplyTo(existingPlayer);
    return Ok(existingPlayer);
}
```

### Combined (`[CreateOrUpdateDto]`)

Generates a single `TeamRequest` with `ValidateForCreate()` and `ValidateForUpdate()`:

```csharp
[CreateOrUpdateDto]
public class Team
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }

    public required string Name { get; set; }
    public string? Description { get; set; }
    public int MaxMembers { get; set; }
}
```

```csharp
[HttpPost]
public IActionResult Create([FromBody] TeamRequest request)
{
    var validation = request.ValidateForCreate();
    if (!validation.IsValid)
        return BadRequest(new { validation.Errors });

    Team team = request.ToEntity();
    return Ok(team);
}

[HttpPatch("{id}")]
public IActionResult Update(int id, [FromBody] TeamRequest request)
{
    var validation = request.ValidateForUpdate();
    if (!validation.IsValid)
        return BadRequest(new { validation.Errors });

    request.ApplyTo(existingTeam);
    return Ok(existingTeam);
}
```

## What gets generated

### Separate

```csharp
public record CreatePlayerRequest
{
    public PatchField<string> Name { get; init; }   // required in Validate()
    public PatchField<int> Level { get; init; }      // optional
    public PatchField<string?> Email { get; init; }  // optional, nullable

    public DtoValidationResult Validate() { ... }
    public Player ToEntity() { ... }
}

public record UpdatePlayerRequest
{
    public PatchField<string> Name { get; init; }    // optional, but non-null if sent
    public PatchField<int> Level { get; init; }
    public PatchField<string?> Email { get; init; }

    public DtoValidationResult Validate() { ... }
    public void ApplyTo(Player target) { ... }
}
```

### Combined

```csharp
public record TeamRequest
{
    public PatchField<string> Name { get; init; }
    public PatchField<string?> Description { get; init; }
    public PatchField<int> MaxMembers { get; init; }

    public DtoValidationResult ValidateForCreate() { ... }
    public DtoValidationResult ValidateForUpdate() { ... }
    public Team ToEntity() { ... }
    public void ApplyTo(Team target) { ... }
}
```

`PatchField<T>` distinguishes three states: **not sent** (`HasValue = false`), **sent with value**, and **sent as null**. It has implicit operators so you can assign and read values directly:

```csharp
// Assignment -- no need for new PatchField<string>("Bob")
var request = new CreatePlayerRequest { Name = "Bob", Level = 5 };

// Reading -- no need for .Value
string name = request.Name;
```

For custom logic (audit logging, conditional business rules), `PatchField<T>` works naturally with C# pattern matching:

```csharp
switch (request.Email)
{
    case { HasValue: false }:               break;  // not sent — leave unchanged
    case { HasValue: true, Value: null }:   player.Email = null; break;  // explicit clear
    case { HasValue: true, Value: var v }:  player.Email = v; break;     // new value
}
```

See the [PatchField Tri-State guide](/ZibStack.NET/guides/patchfield-tri-state/) for the full walkthrough — why nullable DTOs can't distinguish "not sent" from "set to null", and how `PatchField<T>` solves it.

## Interfaces

Generated types implement generic interfaces for type-safe generic handlers:

| Interface | Implemented by | Method |
|---|---|---|
| `ICanCreate<T>` | Create requests, Combined | `T ToEntity()` |
| `ICanApply<T>` | Update requests, Combined | `void ApplyTo(T target)` |
| `ICanValidate` | Create/Update requests (not Combined) | `DtoValidationResult Validate()` |

Combined requests implement `ICanCreate<T>` and `ICanApply<T>` but not `ICanValidate` (they have `ValidateForCreate()`/`ValidateForUpdate()` instead).

```csharp
// Generic handler using interfaces
public IActionResult HandleCreate<T>(ICanCreate<T> request) where T : class
{
    if (request is ICanValidate validatable)
    {
        var validation = validatable.Validate();
        if (!validation.IsValid) return BadRequest(validation.Errors);
    }
    var entity = request.ToEntity();
    return Ok(entity);
}
```

## Attributes

**Class-level attributes:**

| Attribute | Target | Description |
|-----------|--------|-------------|
| `[CreateDto]` | Class | Generates Create request with `Validate()` + `ToEntity()` |
| `[UpdateDto]` | Class | Generates Update request with `Validate()` + `ApplyTo()` |
| `[CreateOrUpdateDto]` | Class | Generates single DTO with `ValidateForCreate/Update()` + both |
| `[ResponseDto]` | Class | Generates read-only Response DTO with `FromEntity()` + `ProjectFrom()` |
| `[QueryDto]` | Class | Generates filter + sort DTO with nullable properties + `ApplyFilter/ApplySort/Apply(IQueryable)`. `Sortable` defaults to `true`. |
| `[QueryDto(Sortable = false)]` | Class | Filter-only DTO — skips `SortBy`, `SortDirection`, `ApplySort()`. For endpoints with a fixed result order. |
| `[CrudApi]` | Class | Generates full CRUD API endpoints + auto-implies missing DTOs |
| `[CreateDtoFor(typeof(T))]` | Record (partial) | Generates create DTO for external type `T` |
| `[UpdateDtoFor(typeof(T))]` | Record (partial) | Generates update DTO for external type `T` |
| `[RenameProperty("From", "To")]` | Class | Renames a source property in DtoFor (maps back in ToEntity/ApplyTo) |

**Property-level attributes:**

| Attribute | Description |
|-----------|-------------|
| `[DtoIgnore]` | Excludes from **all** generated DTOs (equivalent to `DtoTarget.All`) |
| `[DtoIgnore(DtoTarget.X)]` | Excludes from specific DTO targets: `Create`, `Update`, `Response`, `Query`, `List` (combinable with `\|`) |
| `[DtoOnly(DtoTarget.X)]` | Includes **only** in the specified DTO target(s): e.g. `DtoTarget.Create`, `DtoTarget.Update` |
| `[DtoName("json_name")]` | Overrides the JSON property name (works on all DTOs including Response) |
| `[Immutable]` | Visible in Update DTO but skipped in `ApplyTo()` |
| `[Flatten]` | Expands nested object properties into parent DTO (Response only) |

**`[DtoName]` on Response DTO:**

```csharp
[CrudApi]
public partial class Order
{
    [DtoName("Customer")]
    public string CustomerName { get; set; }  // → "Customer" in response

    [DtoName("Total")]
    public decimal TotalAmount { get; set; }  // → "Total" in response
}
```

**Generated types:**

| Type | Description |
|------|-------------|
| `PaginatedResponse<T>` | Generic paginated wrapper with `Items`, `TotalCount`, `Page`, `PageSize` |
| `DtoValidationResult` | Per-property validation errors with `IsValid`, `Errors`, `AddError()`, `Merge()`, `ToDictionary()` |

> **Note:** `[PartialFrom]`, `[IntersectFrom]`, `[PickFrom]`, `[OmitFrom]` are in the separate [`ZibStack.NET.Core`](https://www.nuget.org/packages/ZibStack.NET.Core) package.

### `required` keyword

Properties marked `required` are validated as mandatory in create validation. In update validation, all properties are optional.

### Custom names

```csharp
[CreateDto(Name = "NewPlayerDto")]
[UpdateDto(Name = "EditPlayerDto")]
public class Player { ... }

// Combined
[CreateOrUpdateDto(Name = "PlayerDto")]
public class Player { ... }
```

### Create/Update-only properties

```csharp
[CreateDto]
[UpdateDto]
public class Player
{
    public required string Name { get; set; }

    [DtoOnly(DtoTarget.Create)]
    public required string Password { get; set; }    // only in CreatePlayerRequest

    [DtoOnly(DtoTarget.Update)]
    public string? DeactivationReason { get; set; }  // only in UpdatePlayerRequest
}
```

With `[CreateOrUpdateDto]`, `[DtoOnly(DtoTarget.Create)]` properties are included but excluded from `ValidateForUpdate()` and `ApplyTo()`. `[DtoOnly(DtoTarget.Update)]` properties are excluded from `ValidateForCreate()` and `ToEntity()`.

## External types (`[CreateDtoFor]` / `[UpdateDtoFor]`)

For classes you don't control (e.g. from a NuGet package), use separate attributes for create and update:

```csharp
// External class you can't modify
public class ExternalOrder
{
    public int Id { get; set; }
    public string ProductName { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string? Notes { get; set; }
}

// Create DTO — ignore Id, require ProductName
[CreateDtoFor(typeof(ExternalOrder), Ignore = new[] { "Id" }, Required = new[] { "ProductName" })]
public partial record CreateOrderRequest;

// Update DTO — ignore Id
[UpdateDtoFor(typeof(ExternalOrder), Ignore = new[] { "Id" })]
public partial record UpdateOrderRequest;
```

The generator produces a `partial record` with `PatchField` properties for all non-ignored properties from the target type.

- `[CreateDtoFor]` generates: `Validate()` (checks Required fields) + `ToEntity()`
- `[UpdateDtoFor]` generates: `Validate()` (null checks only) + `ApplyTo(target)`

You control the class name, which properties to ignore, and which to make required.

### Renaming properties

Use `[RenameProperty]` to rename a source property in the generated DTO. `ToEntity()` and `ApplyTo()` map back to the original name:

```csharp
[CreateDtoFor(typeof(ExternalUser), Ignore = new[] { "Id", "LastName" })]
[RenameProperty("FirstName", "Name")]
public partial record CreateUserRequest;
// DTO has "Name" property, ToEntity() writes to "FirstName"
```

## Utility types (from `ZibStack.NET.Core`)

TypeScript-style utility types are available in the separate [`ZibStack.NET.Core`](/ZibStack.NET/packages/core/) package:

```csharp
using ZibStack.NET.Core;

[PartialFrom(typeof(Player))]       // all properties optional + ApplyTo()
public partial record PartialPlayer;

[PickFrom(typeof(Player), "Name", "Level")]  // whitelist
public partial record PlayerSummary;

[OmitFrom(typeof(Player), "Id", "CreatedAt")]  // blacklist
public partial record PlayerWithoutMeta;

[IntersectFrom(typeof(Player))]     // combine multiple types
[IntersectFrom(typeof(Address))]
public partial record PlayerWithAddress;
```

See the [ZibStack.NET.Core documentation](/ZibStack.NET/packages/core/) for full documentation.

## Query / Filter DTO (`[QueryDto]`)

Generates a filter DTO where all properties are nullable, plus an `ApplyFilter(IQueryable<T>)` method:

```csharp
[QueryDto]
public class Product
{
    public string Name { get; set; }
    public decimal Price { get; set; }
    public bool InStock { get; set; }
}
```

```csharp
// Generated
public record ProductQuery
{
    public string? Name { get; init; }
    public decimal? Price { get; init; }
    public bool? InStock { get; init; }

    public IQueryable<Product> ApplyFilter(IQueryable<Product> query) { ... }
}

// Usage
[HttpGet]
public IActionResult List([FromQuery] ProductQuery query)
{
    var results = query.ApplyFilter(_db.Products).ToList();
    return Ok(results);
}
```

### Sorting

`Sortable` defaults to `true`, so `[QueryDto]` already generates `SortBy`, `SortDirection` properties and `ApplySort(IQueryable<T>)`. Set a default sort column with `DefaultSort`:

```csharp
[QueryDto(DefaultSort = "Name")]
public class Product
{
    public string Name { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; set; }
}
```

```csharp
// Generated
public record ProductQuery
{
    public string? Name { get; init; }
    public decimal? Price { get; init; }
    public int? Stock { get; init; }
    public string? SortBy { get; init; }
    public SortDirection? SortDirection { get; init; }

    public IQueryable<Product> ApplyFilter(IQueryable<Product> query) { ... }
    public IQueryable<Product> ApplySort(IQueryable<Product> query) { ... }
    public IQueryable<Product> Apply(IQueryable<Product> query) { ... } // filter + sort
}

// Usage
[HttpGet]
public IActionResult List([FromQuery] ProductQuery query)
{
    var results = query.Apply(_db.Products).ToList();
    return Ok(results);
}
```

`SortBy` is case-insensitive and matches property names. Unknown values are ignored (no sort applied). `DefaultSort` and `DefaultSortDirection` set fallback behavior when `SortBy`/`SortDirection` are not provided.

Set `[QueryDto(Sortable = false)]` only for endpoints with a fixed result order (analytics, ordered exports) where clients should not re-sort.

### Query DSL (with `ZibStack.NET.Query`)

When `ZibStack.NET.Query` is referenced, the generated CRUD list endpoints accept `filter` and `sort` string parameters parsed as a DSL. Dot notation into `[OneToOne]` / `[OneToMany]` relations works automatically — see [Modeling Relationships & Query DSL](/ZibStack.NET/guides/relationships-query-dsl/) for the full walkthrough.

**Filter** — 14 operators, AND/OR/grouping, case insensitive, dot notation:

```
GET /api/players?filter=Level>25,Name=*ski&sort=-Level
GET /api/players?filter=Team.Name=Lakers                    // [OneToOne] dot notation
GET /api/players?filter=(Level<10|Level>90),Team.City=LA    // OR + grouping
GET /api/players?filter=Name=in=Alice;Bob;Diana             // IN list
```

**Collection filtering via `[OneToMany]`:**

```
GET /api/teams?filter=Players.Name=*ski                  // any player name contains "ski"
GET /api/teams?filter=Players.Count>5                    // team has more than 5 players
```

**Count only** — return just the total count without fetching items:

```
GET /api/players?filter=Level>25&count=true → { "count": 42 }
```

**Field selection** — return only specific fields (including nested relations):

```
GET /api/players?select=Name,Level,Team.Name
```

## Paginated response (`PaginatedResponse<T>`)

Generic wrapper for paginated results:

```csharp
// Simple creation
var page = PaginatedResponse<ProductResponse>.Create(items, totalCount: 100, page: 2, pageSize: 10);

// From IQueryable (handles Skip/Take automatically)
var page = await PaginatedResponse<Product>.CreateAsync(_db.Products, page: 1, pageSize: 20);

// Map items (e.g. entity → response DTO)
var response = page.Map(p => ProductResponse.FromEntity(p));

// Properties
page.Items        // IReadOnlyList<T>
page.TotalCount   // int
page.Page         // int
page.PageSize     // int
page.TotalPages   // computed
page.HasNextPage  // computed
page.HasPreviousPage // computed
```

Full example with `[QueryDto]` + `[ResponseDto]`:

```csharp
[HttpGet]
public async Task<IActionResult> List([FromQuery] ProductQuery query, int page = 1, int pageSize = 20)
{
    var filtered = query.Apply(_db.Products);
    var paginated = await PaginatedResponse<Product>.CreateAsync(filtered, page, pageSize);
    var response = paginated.Map(p => ProductResponse.FromEntity(p));
    return Ok(response);
}
```

## CRUD API generation (`[CrudApi]`)

Add `[CrudApi]` to your entity to generate complete CRUD API endpoints. A single attribute is enough — `CreateDto`, `UpdateDto`, and `ResponseDto` are auto-implied when missing:

```csharp
[CrudApi]
public class Player
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Level { get; set; }
    public string? Email { get; set; }
    [DtoIgnore]  public DateTime CreatedAt { get; set; }
}
```

This generates `CreatePlayerRequest`, `UpdatePlayerRequest`, `PlayerResponse`, and full CRUD endpoints — all from one attribute. Add explicit DTO attributes when you need custom names, sorting, or fine-grained control:

```csharp
[CrudApi(Style = ApiStyle.Both)]
[CreateDto(Name = "NewPlayerDto")]            // custom request name
[QueryDto(DefaultSort = "Name")]              // filtering + sorting (Sortable is true by default)
public class Player { ... }
```

This generates two classes (depending on `Style`):

- `PlayerEndpoints` — Minimal API endpoints via `MapPlayerEndpoints()` extension method
- `PlayerCrudController` — MVC `[ApiController]` (partial, so you can extend it)

Both inject `ICrudStore<Player, int>` from DI and wire up: validation → entity mapping → store → response mapping → pagination.

### Generated endpoints

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/api/players/{id}` | Get by ID → `PlayerResponse` |
| `GET` | `/api/players?name=...&sortBy=level&page=2` | List with filter + sort + pagination → `PaginatedResponse<PlayerResponse>` |
| `POST` | `/api/players` | Create → validate → `ToEntity()` → store → 201 |
| `PATCH` | `/api/players/{id}` | Update → validate → `ApplyTo()` → store → 200 |
| `DELETE` | `/api/players/{id}` | Delete → 204 |
| `POST` | `/api/players/bulk` | Bulk create (requires `CrudOperations.BulkCreate`) |
| `POST` | `/api/players/bulk-delete` | Bulk delete by IDs (requires `CrudOperations.BulkDelete`) |

### Generated code (Minimal API)

Here's what the generator actually produces (simplified):

```csharp
// <auto-generated />
public static class PlayerEndpoints
{
    public static RouteGroupBuilder MapPlayerEndpoints(
        this IEndpointRouteBuilder app,
        string? prefix = null,
        Action<RouteGroupBuilder>? configure = null)
    {
        var group = app.MapGroup(prefix ?? "api/players").WithTags("Player");
        configure?.Invoke(group);

        // GET /api/players/{id}
        group.MapGet("{id}", async (int id, ICrudStore<Player, int> store, CancellationToken ct) =>
        {
            var entity = await store.GetByIdAsync(id, ct);
            if (entity is null) return Results.Problem(statusCode: 404, title: "Not Found");
            return Results.Ok(PlayerResponse.FromEntity(entity));
        }).WithName("GetPlayer");

        // GET /api/players?name=...&sortBy=level&page=2
        group.MapGet("", ([AsParameters] PlayerQuery query,
            ICrudStore<Player, int> store, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        {
            var q = query.Apply(store.Query());
            var projected = PlayerResponse.ProjectFrom(q);
            return PaginatedResponse<PlayerResponse>.CreateAsync(projected, page, pageSize, ct);
        }).WithName("GetPlayerList");

        // POST /api/players
        group.MapPost("", async (CreatePlayerRequest request, ICrudStore<Player, int> store, CancellationToken ct) =>
        {
            var validation = request.Validate();
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
            var entity = request.ToEntity();
            await store.CreateAsync(entity, ct);
            return Results.CreatedAtRoute("GetPlayer", new { id = entity.Id },
                PlayerResponse.FromEntity(entity));
        });

        // PATCH /api/players/{id}
        group.MapPatch("{id}", async (int id, UpdatePlayerRequest request, ICrudStore<Player, int> store, CancellationToken ct) =>
        {
            var entity = await store.GetByIdAsync(id, ct);
            if (entity is null) return Results.Problem(statusCode: 404, title: "Not Found");
            var validation = request.Validate();
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
            request.ApplyTo(entity);
            await store.UpdateAsync(entity, ct);
            return Results.Ok(PlayerResponse.FromEntity(entity));
        });

        // DELETE /api/players/{id}
        group.MapDelete("{id}", async (int id, ICrudStore<Player, int> store, CancellationToken ct) =>
        {
            var entity = await store.GetByIdAsync(id, ct);
            if (entity is null) return Results.Problem(statusCode: 404, title: "Not Found");
            await store.DeleteAsync(entity, ct);
            return Results.NoContent();
        });

        return group;
    }
}
```

The generated MVC Controller follows the same pattern with `[HttpGet]`, `[HttpPost]`, `[HttpPatch]`, `[HttpDelete]` attributes and is `partial` so you can extend it.

### Setup

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new PatchFieldJsonConverterFactory()));

// Register your data store implementation
builder.Services.AddScoped<ICrudStore<Player, int>, PlayerStore>();

var app = builder.Build();
app.MapPlayerEndpoints();   // generated Minimal API
app.MapControllers();       // picks up generated PlayerCrudController
app.Run();
```

### `ICrudStore<TEntity, TKey>`

The generated endpoints depend on this interface for data access. Implement it for your storage layer:

```csharp
public interface ICrudStore<TEntity, TKey>
{
    ValueTask<TEntity?> GetByIdAsync(TKey id, CancellationToken ct = default);
    IQueryable<TEntity> Query();
    ValueTask CreateAsync(TEntity entity, CancellationToken ct = default);
    ValueTask UpdateAsync(TEntity entity, CancellationToken ct = default);
    ValueTask DeleteAsync(TEntity entity, CancellationToken ct = default);
}
```

### Storage integrations

Ready-made implementations are available as separate packages:

| Package | Description |
|---------|-------------|
| [`ZibStack.NET.EntityFramework`](https://www.nuget.org/packages/ZibStack.NET.EntityFramework) | EF Core — auto-generates stores + DI registration from `DbContext` |
| [`ZibStack.NET.Dapper`](https://www.nuget.org/packages/ZibStack.NET.Dapper) | Dapper — base class with auto-generated SQL |

**EF Core** — add `[GenerateCrudStores]` to your DbContext and the store implementations + DI registration are generated automatically:

```
dotnet add package ZibStack.NET.EntityFramework
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

```csharp
[GenerateCrudStores]
public class AppDbContext : DbContext
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Team> Teams => Set<Team>();
    // ...
}
```

```csharp
// In Program.cs — one line registers all stores
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite("Data Source=app.db"));
builder.Services.AddAppDbContextCrudStores();  // auto-generated extension method
```

You can also implement `ICrudStore<T,K>` manually or inherit `EfCrudStore<T,K,TContext>` for custom behavior.

### Auto-implied DTOs

`[CrudApi]` alone is enough for a full CRUD API. Missing DTO attributes are auto-generated with sensible defaults:

```csharp
[CrudApi]  // generates CreatePlayerRequest, UpdatePlayerRequest, PlayerResponse + endpoints
public class Player
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Level { get; set; }
}
```

Explicit DTO attributes always take priority when present:

| What's on the class | What gets generated |
|---|---|
| `[CrudApi]` alone | Auto: `CreateXxxRequest`, `UpdateXxxRequest`, `XxxResponse` |
| `[CrudApi]` + `[CreateDto(Name = "NewPlayer")]` | Explicit `NewPlayer` + auto: Update, Response |
| `[CrudApi]` + `[CreateOrUpdateDto]` | Explicit combined DTO + auto: Response |
| `[CrudApi]` + all DTO attrs | All explicit — auto-imply disabled |

### Attribute options

```csharp
[CrudApi(
    Route = "api/v2/players",              // full route override (default: api/{pluralized-name})
    RoutePrefix = "v2",                    // prefix only → api/v2/players (ignored when Route is set)
    KeyProperty = "PlayerId",              // key property name (default: "Id")
    Operations = CrudOperations.AllWithBulk, // which operations (default: All)
    Style = ApiStyle.MinimalApi,           // MinimalApi, Controller, or Both
    AuthorizePolicy = "read:players",      // default policy for all operations
    CreatePolicy = "write:players",        // override for POST only
    UpdatePolicy = "write:players",        // override for PATCH only
    DeletePolicy = "admin",               // override for DELETE only
    GetByIdPolicy = "read:players",        // override for GET by ID
    GetListPolicy = "read:players"         // override for GET list
)]
```

Per-operation policies override the default `AuthorizePolicy` for specific operations. This allows read-only access for some users while requiring elevated permissions for writes.

**`CrudOperations`** flags:

| Flag | Value | Description |
|------|-------|-------------|
| `GetById` | 1 | GET by ID |
| `GetList` | 2 | GET list |
| `Create` | 4 | POST |
| `Update` | 8 | PATCH |
| `Delete` | 16 | DELETE |
| `BulkCreate` | 32 | POST /bulk |
| `BulkDelete` | 64 | POST /bulk-delete |
| `Read` | 3 | GetById + GetList |
| `Write` | 28 | Create + Update + Delete |
| `Bulk` | 96 | BulkCreate + BulkDelete |
| `All` | 31 | Read + Write (no bulk) |
| `AllWithBulk` | 127 | All + Bulk |

### Customizing endpoints

**Property-level control** — attributes on model properties affect what appears in generated requests/responses:

| Attribute | Effect on CRUD |
|-----------|---------------|
| `[DtoIgnore]` | Excluded from **all** request and response DTOs |
| `[DtoIgnore(DtoTarget.X)]` | Excluded from specific DTO targets (e.g. `DtoTarget.Response`, `DtoTarget.Query`, `DtoTarget.List`) |
| `[DtoOnly(DtoTarget.X)]` | Included **only** in the specified target (e.g. `DtoTarget.Create` for POST-only, `DtoTarget.Update` for PATCH-only) |
| `[Immutable]` | Visible in PATCH DTO but `ApplyTo()` skips it |
| `[Flatten]` | Nested object properties flattened into response |
| `required` | Validated as mandatory in POST, optional in PATCH |
| `[ZRequired]`, `[ZMinLength]`, `[ZMaxLength]`, `[ZRange]`, `[ZEmail]`, `[ZMatch]` | Propagated to generated validation |

**Custom DTO names:**

```csharp
[CrudApi]
[CreateDto(Name = "NewPlayerDto")]    // override default CreatePlayerRequest
[ResponseDto(Name = "PlayerView")]    // override default PlayerResponse
```

**Minimal API route group configuration** — the generated `MapXxxEndpoints` accepts a `configure` callback:

```csharp
app.MapPlayerEndpoints(configure: group =>
{
    group.RequireRateLimiting("fixed");
    group.AddEndpointFilter<MyLoggingFilter>();
    group.WithTags("Players", "V1");
});
```

**Custom route prefix** — override the generated route or add a version prefix:

```csharp
app.MapPlayerEndpoints(prefix: "api/v2/players");  // override route at runtime
```

**Extending generated controllers** — generated controllers are `partial`, so you can add extra endpoints:

```csharp
public partial class PlayerCrudController
{
    [HttpPost("bulk")]
    public async Task<IActionResult> BulkCreate([FromBody] List<CreatePlayerRequest> requests, CancellationToken ct)
    {
        // custom bulk logic
    }
}
```

**Custom data access** — override methods in `EfCrudStore` for custom behavior:

```csharp
public class PlayerStore : EfCrudStore<Player, int, AppDbContext>
{
    public PlayerStore(AppDbContext db) : base(db) { }
    protected override DbSet<Player> Set => Db.Players;

    // Soft delete instead of hard delete
    public override async ValueTask DeleteAsync(Player entity, CancellationToken ct = default)
    {
        entity.IsDeleted = true;
        await Db.SaveChangesAsync(ct);
    }

    // Auto-set timestamps
    public override async ValueTask CreateAsync(Player entity, CancellationToken ct = default)
    {
        entity.CreatedAt = DateTime.UtcNow;
        await base.CreateAsync(entity, ct);
    }
}
```

### Error responses

All error responses use the [RFC 9110](https://tools.ietf.org/html/rfc9110) **ProblemDetails** format (`application/problem+json`):

```json
// Validation error (400)
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "name": ["is required."],
    "password": ["is required."]
  }
}

// Not found (404)
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404
}
```

### List vs Detail responses (`[DtoIgnore(DtoTarget.List)]`)

Mark properties with `[DtoIgnore(DtoTarget.List)]` to exclude them from the GET list response while keeping them in the GET by ID response. The generator creates a separate `{Entity}ListItem` DTO for list endpoints:

```csharp
[CrudApi]
public class Player
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Level { get; set; }

    [DtoIgnore(DtoTarget.List)]
    public string? Bio { get; set; }          // only in GET /api/players/{id}

    [DtoIgnore(DtoTarget.List)]
    public Address? Address { get; set; }      // only in GET /api/players/{id}
}
```

Generated: `PlayerResponse` (all fields, for detail) and `PlayerListItem` (without Bio/Address, for list).

### Bulk operations

Enable bulk create/delete with `CrudOperations.Bulk` or `CrudOperations.AllWithBulk`:

```csharp
[CrudApi(Operations = CrudOperations.AllWithBulk)]
public class Player { ... }
```

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/players/bulk` | POST | Create multiple entities. Body: `[{...}, {...}]`. Validates all before creating. |
| `/api/players/bulk-delete` | POST | Delete by IDs. Body: `[1, 3, 5]`. Returns `{"deleted": 2}`. |

Bulk endpoints inherit the same per-operation auth policies (`CreatePolicy` for bulk create, `DeletePolicy` for bulk delete).

### Conditional emission

CRUD endpoints are only generated when the consuming project references ASP.NET Core (detected at compile time). This means:

- Library projects that only use DTOs → no endpoint code emitted
- Web API projects → full CRUD generation
- No extra dependencies needed in the generator package

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| SDTO011 | Error | `KeyProperty` value does not match any property on the type |

## `ApplyWithChanges()` (Update DTOs only)

Like `ApplyTo()` but returns a tuple with the list of actually changed field names. Available on Update, Combined, and UpdateDtoFor requests:

```csharp
var (changedFields, entity) = request.ApplyWithChanges(existingProduct);
// changedFields: ["price", "stock"]
// Useful for audit logs, webhooks, selective cache invalidation
```

### Response DTO (`[ResponseDto]`)

Generates a read-only record for GET responses with `FromEntity()` and IQueryable `ProjectFrom()`:

```csharp
[CreateDto]
[UpdateDto]
[ResponseDto]
public class Player
{
    public int Id { get; set; }
    public required string Name { get; set; }
    
    [DtoIgnore(DtoTarget.Response)]
    public required string Password { get; set; }
}
```

```csharp
// Generated — plain properties, no PatchField
public record PlayerResponse
{
    public int Id { get; init; }       // DtoIgnore(DtoTarget.Create|Update|Query) doesn't affect Response
    public string Name { get; init; }
    // Password excluded by [DtoIgnore(DtoTarget.Response)]
    
    public static PlayerResponse FromEntity(Player entity) => ...;
    public static IQueryable<PlayerResponse> ProjectFrom(IQueryable<Player> query) => ...;
}

// Usage
[HttpGet("{id}")]
public IActionResult Get(int id)
{
    var player = _db.Players.Find(id);
    return Ok(PlayerResponse.FromEntity(player));
}

// EF Core projection — only fetches needed columns
[HttpGet]
public IActionResult List()
{
    var responses = PlayerResponse.ProjectFrom(_db.Players).ToList();
    return Ok(responses);
}
```

### Auto-recursive nested DTOs

When a model has `[CreateDto]` or `[UpdateDto]`, nested complex type properties automatically get their own DTOs generated — **no need to annotate nested types**. This works recursively to any depth, with deduplication (if a nested type already has an explicit `[UpdateDto]`, its DTO is reused).

#### 3-level example — Employee → Company → ContactInfo

```csharp
// Your models — only the top level has attributes:
[CreateDto]
[UpdateDto]
public class Employee
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)] public int Id { get; set; }
    public required string Name { get; set; }
    public Company? Company { get; set; }          // Level 2 — auto-generated
}

public class Company
{
    public required string Name { get; set; }
    public ContactInfo? Contact { get; set; }      // Level 3 — auto-generated
}

public class ContactInfo
{
    public required string Phone { get; set; }
    public string? Fax { get; set; }
}
```

The generator produces **three** Update request records from a single `[UpdateDto]`:

```csharp
// Generated — Level 1
public record UpdateEmployeeRequest : ICanApply<Employee>, ICanValidate
{
    public PatchField<string> Name { get; init; }
    public PatchField<UpdateCompanyRequest?> Company { get; init; }   // nested DTO, not Company

    public void ApplyTo(Employee target)
    {
        if (Name.HasValue) target.Name = Name.Value!;
        if (Company.HasValue)
        {
            if (Company.Value is null)
                target.Company = null;                                 // explicit clear
            else if (target.Company is not null)
                Company.Value.ApplyTo(target.Company);                 // recursive partial update
        }
    }
}

// Generated — Level 2 (auto, no attribute on Company)
public record UpdateCompanyRequest : ICanApply<Company>, ICanValidate
{
    public PatchField<string> Name { get; init; }
    public PatchField<UpdateContactInfoRequest?> Contact { get; init; }

    public void ApplyTo(Company target)
    {
        if (Name.HasValue) target.Name = Name.Value!;
        if (Contact.HasValue)
        {
            if (Contact.Value is null)
                target.Contact = null;
            else if (target.Contact is not null)
                Contact.Value.ApplyTo(target.Contact);                 // chain continues
        }
    }
}

// Generated — Level 3 (auto, leaf)
public record UpdateContactInfoRequest : ICanApply<ContactInfo>, ICanValidate
{
    public PatchField<string> Phone { get; init; }
    public PatchField<string?> Fax { get; init; }

    public void ApplyTo(ContactInfo target)
    {
        if (Phone.HasValue) target.Phone = Phone.Value!;
        if (Fax.HasValue) target.Fax = Fax.Value;
    }
}
```

Now a 3-level-deep partial update is a single PATCH:

```json
PATCH /api/employees/1
{
  "company": {
    "contact": {
      "fax": null
    }
  }
}
```

Only `employee.Company.Contact.Fax` is cleared. `Company.Name`, `Contact.Phone`, `Employee.Name` — all untouched. Each level's `ApplyTo` checks `HasValue` independently, so the partial-update semantics compose naturally without any manual wiring.

#### Create DTOs — same recursive pattern

`[CreateDto]` follows the same structure, but with `ToEntity()` that chains construction:

```csharp
// Generated
public record CreateEmployeeRequest : ICanCreate<Employee>, ICanValidate
{
    public PatchField<string> Name { get; init; }
    public PatchField<CreateCompanyRequest?> Company { get; init; }

    public Employee ToEntity()
    {
        return new Employee
        {
            Name = Name.HasValue ? Name.Value! : default!,
            Company = Company.HasValue && Company.Value is not null
                ? Company.Value.ToEntity()     // recursive construction
                : default,
        };
    }
}
```

#### Key patterns in the generated code

1. **`PatchField<UpdateXxxRequest?>` wrapping** — the nested type in the parent DTO is the *generated request*, not the original entity. This is what makes tri-state tracking recursive: `Company.HasValue == false` means "don't touch Company at all", `Company.Value == null` means "clear Company", `Company.Value != null` means "apply partial changes to Company's fields".

2. **Null-safe `ApplyTo` chaining** — the generator emits `if (target.Company is not null)` before calling the nested `ApplyTo`. If the parent's navigation is null and the client sends a partial update to it, the update is silently skipped (you can't `ApplyTo` a null target). To create a new nested object from a PATCH, the client should use a full object value, not a partial one.

3. **Deduplication** — if `ContactInfo` is used in multiple parent types (`Employee.Company.Contact` and `Project.Lead`), the generator emits `UpdateContactInfoRequest` once and reuses it everywhere.

4. **`ProjectFrom()` skips nested properties** — in the Response DTO, `ProjectFrom()` (LINQ-to-SQL projection) does not project nested objects because EF Core requires `.Include()` for navigation properties. Use `FromEntity()` with `.Include()` for nested responses.

### Nested type mapping in Response

When a property's type also has `[ResponseDto]`, the generator uses the nested response DTO and maps via `FromEntity()` with null checks:

```csharp
// Generated
public record OrderResponse
{
    public int Id { get; init; }
    public string Title { get; init; }
    public OrderLineResponse? Line { get; init; }

    public static OrderResponse FromEntity(Order entity)
    {
        return new OrderResponse
        {
            Id = entity.Id,
            Title = entity.Title,
            Line = entity.Line is not null
                ? OrderLineResponse.FromEntity(entity.Line)    // null-safe nested mapping
                : null,
        };
    }

    public static IQueryable<OrderResponse> ProjectFrom(IQueryable<Order> query)
    {
        return query.Select(x => new OrderResponse
        {
            Id = x.Id,
            Title = x.Title,
            // Line is NOT projected — use FromEntity() with .Include(x => x.Line) instead
        });
    }
}
```

`ProjectFrom()` is EF Core-safe (no navigation property access in the LINQ expression). For nested data, load via `Include` and map with `FromEntity`:

```csharp
var order = await db.Orders.Include(o => o.Line).FirstAsync(o => o.Id == id);
return OrderResponse.FromEntity(order);   // nested Line is mapped via OrderLineResponse.FromEntity
```

### Flatten nested properties (`[Flatten]`)

Collapses nested object properties into the parent DTO:

```csharp
[ResponseDto]
public class Store
{
    public string Name { get; set; }
    
    [Flatten]
    public Address? Location { get; set; }
}
// Generated StoreResponse has: LocationStreet, LocationCity, LocationZipCode
// FromEntity maps: entity.Location?.Street → LocationStreet
```

### Validation attribute propagation

Attributes from `System.ComponentModel.DataAnnotations` are automatically copied to generated DTOs:

```csharp
public class User
{
    [ZMaxLength(100)]
    [ZEmail]
    public required string Email { get; set; }

    [ZRange(1, 999)]
    public int Quantity { get; set; }
}
// Generated CreateUserRequest.Email has [ZMaxLength(100)] and [ZEmail]
```

### `[Immutable]` properties

Properties marked `[Immutable]` are included in the Update DTO but skipped in `ApplyTo()`:

```csharp
[CreateDto]
[UpdateDto]
public class Article
{
    public required string Title { get; set; }
    
    [Immutable]
    public required string Slug { get; set; }  // can set at creation, never changed
}
```

### `Diff(T entity)` method

Update DTOs include `Diff()` — compares request with an entity and returns changed field names:

```csharp
var changes = request.Diff(existingProduct);
// ["price", "stock"] — useful for audit logs

if (changes.Count == 0) return NoContent(); // nothing actually changed
```

### `DtoMapper`

Generic runtime mapper for copying properties between objects by matching names:

```csharp
var copy = DtoMapper.Map<Product, ProductDto>(product);
DtoMapper.MapTo(source, target);
```

### Swagger / OpenAPI support

When `Swashbuckle.AspNetCore` is detected at compile time, the generator **automatically** emits a `PatchFieldSchemaFilter` that unwraps `PatchField<T>` to its inner type in the Swagger schema — no manual registration needed. Just install the package:

```bash
dotnet add package Swashbuckle.AspNetCore
```

Without Swashbuckle, `PatchField<T>` shows as `{ "hasValue": true, "value": "Bob" }` in the OpenAPI schema. With it, the schema filter collapses it to just `"Bob"` (or `null | "Bob"` for nullable types).

> Both Swashbuckle legacy (pre-v10) and v10+ with `IOpenApiSchema` are supported — the generator detects the API surface at compile time and emits the correct filter variant.

## JSON serializer support

The generator detects which serializers are available and produces the corresponding converters:

| Serializer | Generated converter | Registration |
|---|---|---|
| System.Text.Json | `PatchFieldJsonConverterFactory` | `options.Converters.Add(new PatchFieldJsonConverterFactory())` |
| Newtonsoft.Json | `PatchFieldNewtonsoftConverter` | `settings.Converters.Add(new PatchFieldNewtonsoftConverter())` |

Both are generated if both packages are referenced. No converter is auto-registered -- you choose which one to use, or write your own.

## Custom validation

### Simple validator

Implement `IDtoValidator<T>` and point to it from the attribute:

```csharp
public class MyCreateValidator : IDtoValidator<CreatePlayerRequest>
{
    public DtoValidationResult Validate(CreatePlayerRequest instance)
    {
        var result = new DtoValidationResult();
        if (instance.Name.HasValue && instance.Name.Value.Length < 3)
            result.AddError("name", "must be at least 3 characters.");
        return result;
    }
}

[CreateDto(Validator = typeof(MyCreateValidator))]
public class Player { ... }
```

When a validator is set, `Validate()` delegates entirely to it -- the default generated rules are replaced.

### FluentValidation

When FluentValidation is installed, the generator additionally produces:

- `FluentDtoValidator<T>` -- base class bridging FluentValidation with `IDtoValidator<T>`
- `{RequestName}CreateBaseValidator` -- contains the generated required/null rules
- `{RequestName}UpdateBaseValidator` -- contains the generated null rules

Inherit to extend:

```csharp
public class MyCreateValidator : CreatePlayerRequestCreateBaseValidator
{
    public MyCreateValidator()
    {
        RuleFor(x => x.Name)
            .Must(f => !f.HasValue || f.Value.Length >= 3)
            .WithMessage("Name must be at least 3 characters.");
    }
}

[CreateDto(Validator = typeof(MyCreateValidator))]
public class Player { ... }
```

Or start from scratch:

```csharp
public class MyCreateValidator : FluentDtoValidator<CreatePlayerRequest>
{
    public MyCreateValidator()
    {
        // your rules only
    }
}
```

## Related guides

- [Full CRUD with SQLite](/ZibStack.NET/guides/crud-sqlite/) — end-to-end project with `[CrudApi]`, relationships, Query DSL, observability, and PatchField tri-state demo
- [Modeling Relationships & Query DSL](/ZibStack.NET/guides/relationships-query-dsl/) — `[OneToOne]` / `[OneToMany]`, dot-notation filtering, every DSL operator with SQL translations
- [PatchField Tri-State](/ZibStack.NET/guides/patchfield-tri-state/) — the null-vs-missing problem and `PatchField<T>` with pattern-matching handlers
- [Declarative Observability](/ZibStack.NET/guides/observability/) — `[Log]` + `[Trace]` for structured logging and OpenTelemetry spans on CRUD stores

## Requirements

- .NET 6+ (or .NET Framework with SDK-style projects and System.Text.Json NuGet)
- `required` keyword needs C# 11 / .NET 7+ (optional — without it all properties are optional in Create)

## License

MIT
