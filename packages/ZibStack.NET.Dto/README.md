# ZibStack.NET.Dto

A C# source generator that produces strongly-typed **Create**, **Update**, **Response**, and **Query** DTOs from your domain models, and optionally generates **full CRUD API endpoints** (Minimal API + MVC Controllers). No reflection, no runtime overhead. Supports generics, inheritance, nested types, flattening, validation propagation, and more.

## Install

```
dotnet add package ZibStack.NET.Dto
```

## Quick start

Mark your model with `[CreateDto]` and/or `[UpdateDto]`:

```csharp
using ZibStack.NET.Dto;

[CreateDto]
[UpdateDto]
public class Player
{
    [DtoIgnore]
    public int Id { get; set; }

    public required string Name { get; set; }
    public int Level { get; set; }
    public string? Email { get; set; }
}
```

Register the JSON converter:

```csharp
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
    var errors = request.Validate();
    if (errors.Count > 0)
        return BadRequest(new { errors });

    Player player = request.ToEntity();
    return Ok(player);
}

[HttpPatch("{id}")]
public IActionResult Update(int id, [FromBody] UpdatePlayerRequest request)
{
    var errors = request.Validate();
    if (errors.Count > 0)
        return BadRequest(new { errors });

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
    [DtoIgnore]
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
    var errors = request.ValidateForCreate();
    if (errors.Count > 0)
        return BadRequest(new { errors });

    Team team = request.ToEntity();
    return Ok(team);
}

[HttpPatch("{id}")]
public IActionResult Update(int id, [FromBody] TeamRequest request)
{
    var errors = request.ValidateForUpdate();
    if (errors.Count > 0)
        return BadRequest(new { errors });

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

    public List<string> Validate() { ... }
    public Player ToEntity() { ... }
}

public record UpdatePlayerRequest
{
    public PatchField<string> Name { get; init; }    // optional, but non-null if sent
    public PatchField<int> Level { get; init; }
    public PatchField<string?> Email { get; init; }

    public List<string> Validate() { ... }
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

    public List<string> ValidateForCreate() { ... }
    public List<string> ValidateForUpdate() { ... }
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

## Interfaces

Generated types implement generic interfaces for type-safe generic handlers:

| Interface | Implemented by | Method |
|---|---|---|
| `ICanCreate<T>` | Create requests, Combined | `T ToEntity()` |
| `ICanApply<T>` | Update requests, Combined | `void ApplyTo(T target)` |
| `ICanValidate` | Create/Update requests (not Combined) | `List<string> Validate()` |

Combined requests implement `ICanCreate<T>` and `ICanApply<T>` but not `ICanValidate` (they have `ValidateForCreate()`/`ValidateForUpdate()` instead).

```csharp
// Generic handler using interfaces
public IActionResult HandleCreate<T>(ICanCreate<T> request) where T : class
{
    if (request is ICanValidate validatable)
    {
        var errors = validatable.Validate();
        if (errors.Count > 0) return BadRequest(errors);
    }
    var entity = request.ToEntity();
    return Ok(entity);
}
```

## Attributes

| Attribute | Target | Description |
|-----------|--------|-------------|
| `[CreateDto]` | Class | Generates Create request with `Validate()` + `ToEntity()` |
| `[UpdateDto]` | Class | Generates Update request with `Validate()` + `ApplyTo()` |
| `[CreateOrUpdateDto]` | Class | Generates single DTO with `ValidateForCreate/Update()` + both |
| `[ResponseDto]` | Class | Generates read-only Response DTO with `FromEntity()` + `ProjectFrom()` |
| `[CreateDtoFor(typeof(T))]` | Record (partial) | Generates create DTO for external type `T` with `Validate()` + `ToEntity()` |
| `[UpdateDtoFor(typeof(T))]` | Record (partial) | Generates update DTO for external type `T` with `Validate()` + `ApplyTo()` |
| `[PickFrom(typeof(T), ...)]` | Record (partial) | Like TS `Pick<T, K>` — whitelist of properties |
| `[OmitFrom(typeof(T), ...)]` | Record (partial) | Like TS `Omit<T, K>` — exclude listed properties |
| `[QueryDto]` | Class | Generates filter DTO with nullable properties + `ApplyFilter(IQueryable)` |
| `[QueryDto(Sortable = true)]` | Class | Adds `SortBy`, `SortDirection`, `ApplySort()`, `Apply()` to query DTO |
| `[CrudApi]` | Class | Generates full CRUD API endpoints (Minimal API and/or Controller) using `ICrudStore<T,TKey>` |
| `PaginatedResponse<T>` | — | Generic paginated wrapper with `Items`, `TotalCount`, `Page`, `PageSize` |
| `[PartialFrom(typeof(T))]` | Record (partial) | Generates `PatchField` properties + `ApplyTo()` for all properties of `T` |
| `[IntersectFrom(typeof(T))]` | Record (partial) | Combine multiple types into one (like TS `&`). Apply multiple times. |
| `[DtoIgnore]` | Property | Excludes from generated DTOs |
| `[DtoName("json_name")]` | Property | Overrides the JSON property name |
| `[CreateOnly]` | Property | Only included in Create (or `ValidateForCreate`/`ToEntity` in Combined) |
| `[UpdateOnly]` | Property | Only included in Update (or `ValidateForUpdate`/`ApplyTo` in Combined) |
| `[Immutable]` | Property | Visible in Update DTO but skipped in `ApplyTo()` |
| `[Flatten]` | Property | Expands nested object properties into parent DTO (Response only) |
| `[ResponseIgnore]` | Property | Excludes from Response DTO only |
| `[RenameProperty("From", "To")]` | Class | Renames a source property in DtoFrom (maps back in ToEntity/ApplyTo) |

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

    [CreateOnly]
    public required string Password { get; set; }    // only in CreatePlayerRequest

    [UpdateOnly]
    public string? DeactivationReason { get; set; }  // only in UpdatePlayerRequest
}
```

With `[CreateOrUpdateDto]`, `[CreateOnly]` properties are included but excluded from `ValidateForUpdate()` and `ApplyTo()`. `[UpdateOnly]` properties are excluded from `ValidateForCreate()` and `ToEntity()`.

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

## Partial types (`[PartialFrom]`)

Like TypeScript's `Partial<T>` -- generates a class where every property is optional:

```csharp
[PartialFrom(typeof(Player))]
public partial record PartialPlayer;
```

Generates:

```csharp
public partial record PartialPlayer
{
    public PatchField<string> Name { get; init; }
    public PatchField<int> Level { get; init; }
    public PatchField<string?> Email { get; init; }
    // ... all public properties from Player

    public void ApplyTo(Player target) { ... }
}
```

All properties from the target type are included (no `[DtoIgnore]` filtering). The record is `partial` so you can add your own methods and properties. No `Validate()` or `ToEntity()` -- just `ApplyTo()`.

## Pick / Omit (TypeScript-style)

### `[PickFrom]` — whitelist properties

```csharp
[PickFrom(typeof(Player), nameof(Player.Name), nameof(Player.Level))]
public partial record PlayerSummary;
// Only Name and Level — with ApplyTo(Player)
```

### `[OmitFrom]` — exclude properties

```csharp
[OmitFrom(typeof(Player), nameof(Player.Id), nameof(Player.CreatedAt))]
public partial record PlayerWithoutMeta;
// All properties except Id and CreatedAt
```

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

### Sortable queries

Add `Sortable = true` to get `SortBy`, `SortDirection` properties and `ApplySort(IQueryable<T>)`:

```csharp
[QueryDto(Sortable = true, DefaultSort = "Name")]
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

Add `[CrudApi]` to your entity alongside other DTO attributes to generate complete CRUD API endpoints — Minimal API, MVC Controller, or both:

```csharp
[CreateDto]
[UpdateDto]
[ResponseDto]
[QueryDto(Sortable = true, DefaultSort = "Name")]
[CrudApi(Style = ApiStyle.Both)]
public class Player
{
    [DtoIgnore]  public int Id { get; set; }
    public required string Name { get; set; }
    public int Level { get; set; }
    public string? Email { get; set; }
    [DtoIgnore]  public DateTime CreatedAt { get; set; }
}
```

This generates:

- `PlayerEndpoints` — static class with `MapPlayerEndpoints()` extension method (Minimal API)
- `PlayerCrudController` — partial `[ApiController]` (MVC)

Both use `ICrudStore<Player, int>` from DI and wire up the full pipeline: validation → entity mapping → store → response mapping → pagination.

### Generated endpoints

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/api/players/{id}` | Get by ID → `PlayerResponse` |
| `GET` | `/api/players?name=...&sortBy=level&page=2` | List with filter + sort + pagination → `PaginatedResponse<PlayerResponse>` |
| `POST` | `/api/players` | Create → validate → `ToEntity()` → store |
| `PATCH` | `/api/players/{id}` | Update → validate → `ApplyTo()` → store |
| `DELETE` | `/api/players/{id}` | Delete |

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

**EF Core** — when EF Core is referenced, the generator emits an `EfCrudStore<TEntity, TKey, TContext>` base class:

```csharp
public class PlayerStore : EfCrudStore<Player, int, AppDbContext>
{
    public PlayerStore(AppDbContext db) : base(db) { }
    protected override DbSet<Player> Set => Db.Players;
}
```

### Smart DTO detection

The generator analyzes which DTO attributes are present and adapts the generated endpoints:

| Attributes on entity | Effect |
|---|---|
| `[ResponseDto]` | GET endpoints return `{Entity}Response` via `FromEntity()` / `ProjectFrom()` |
| No `[ResponseDto]` | GET endpoints return raw entity |
| `[QueryDto]` | GET list uses query parameters for filtering + sorting via `Apply()` |
| No `[QueryDto]` | GET list returns all items (with pagination only) |
| `[CreateDto]` or `[CreateOrUpdateDto]` | POST endpoint generated |
| `[UpdateDto]` or `[CreateOrUpdateDto]` | PATCH endpoint generated |
| `[CreateOrUpdateDto]` | Uses `ValidateForCreate()` / `ValidateForUpdate()` instead of `Validate()` |
| No create/update DTOs | Write endpoints not generated (warning SDTO010) |

### Attribute options

```csharp
[CrudApi(
    Route = "api/v2/players",              // custom route (default: api/{pluralized-name})
    KeyProperty = "PlayerId",              // key property name (default: "Id")
    Operations = CrudOperations.Read,      // which operations (default: All)
    Style = ApiStyle.MinimalApi,           // MinimalApi, Controller, or Both
    AuthorizePolicy = "admin"              // adds RequireAuthorization / [Authorize]
)]
```

**`CrudOperations`** flags:

| Flag | Value | Description |
|------|-------|-------------|
| `GetById` | 1 | GET by ID |
| `GetList` | 2 | GET list |
| `Create` | 4 | POST |
| `Update` | 8 | PATCH |
| `Delete` | 16 | DELETE |
| `Read` | 3 | GetById + GetList |
| `Write` | 28 | Create + Update + Delete |
| `All` | 31 | Everything |

### Conditional emission

CRUD endpoints are only generated when the consuming project references ASP.NET Core (detected at compile time). The `EfCrudStore` base class is only emitted when EF Core is referenced. This means:

- Library projects that only use DTOs → no endpoint code emitted
- Web API projects → full CRUD generation
- No extra dependencies needed in the generator package

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| SDTO009 | Warning | `[CrudApi]` without `[ResponseDto]` — GET endpoints return raw entity |
| SDTO010 | Warning | `[CrudApi]` without any create/update DTO — write endpoints not generated |
| SDTO011 | Error | `KeyProperty` value does not match any property on the type |

## `ApplyWithChanges()` (Update DTOs only)

Like `ApplyTo()` but returns a tuple with the list of actually changed field names. Available on Update, Combined, and UpdateDtoFor requests:

```csharp
var (changedFields, entity) = request.ApplyWithChanges(existingProduct);
// changedFields: ["price", "stock"]
// Useful for audit logs, webhooks, selective cache invalidation
```

## Intersection types (`[IntersectFrom]`)

Like TypeScript's `&` operator -- combine properties from multiple types into one:

```csharp
[IntersectFrom(typeof(Player))]
[IntersectFrom(typeof(Address))]
public partial record PlayerWithAddress;
```

Generates a single record with all properties from both types (deduplicated by name), and a separate `ApplyTo()` overload for each source type:

```csharp
public partial record PlayerWithAddress
{
    public PatchField<string> Name { get; init; }    // from Player
    public PatchField<int> Level { get; init; }       // from Player
    public PatchField<string> Street { get; init; }   // from Address
    public PatchField<string> City { get; init; }     // from Address
    // ...

    public void ApplyTo(Player target) { ... }
    public void ApplyTo(Address target) { ... }
}
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
    
    [ResponseIgnore]
    public required string Password { get; set; }
}
```

```csharp
// Generated — plain properties, no PatchField
public record PlayerResponse
{
    public int Id { get; init; }       // DtoIgnore doesn't affect Response
    public string Name { get; init; }
    // Password excluded by [ResponseIgnore]
    
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

When a model has `[CreateDto]` or `[UpdateDto]`, nested complex type properties automatically get their own DTOs generated — **no need to annotate nested types**:

```csharp
[UpdateDto]
public class Player
{
    public required string Name { get; set; }
    public Address? Address { get; set; }  // Address has NO attributes
}

public class Address
{
    public string Street { get; set; }
    public string City { get; set; }
}
```

Generates both `UpdatePlayerRequest` and `UpdateAddressRequest`. Partial updates work recursively:

```json
{ "address": { "city": "New York" } }
```
Changes only `city` — `street` stays unchanged.

If the nested type already has an explicit `[UpdateDto]`, its DTO is reused instead of auto-generating. Deep nesting (A → B → C) works recursively with deduplication.

### Nested type mapping in Response

When a property's type also has `[ResponseDto]`, the generator uses the nested response DTO and maps via `FromEntity()`:

```csharp
[ResponseDto]
public class Player
{
    public Address? Address { get; set; }
}

[ResponseDto]
public class Address
{
    public string Street { get; set; }
    public string City { get; set; }
}
```

Generated `PlayerResponse` has `AddressResponse? Address` and `FromEntity()` maps nested objects automatically. `ProjectFrom()` skips nested properties (use `FromEntity()` with `.Include()` for nested EF Core queries).

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
    [MaxLength(100)]
    [EmailAddress]
    public required string Email { get; set; }

    [Range(1, 999)]
    public int Quantity { get; set; }
}
// Generated CreateUserRequest.Email has [MaxLength(100)] and [EmailAddress]
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

When Swashbuckle is detected, the generator emits a `PatchFieldSchemaFilter` that unwraps `PatchField<T>` to its inner type in the Swagger schema:

```csharp
builder.Services.AddSwaggerGen(c => c.SchemaFilter<PatchFieldSchemaFilter>());
```

Without this, Swagger shows `{ "hasValue": true, "value": "Bob" }` instead of just `"Bob"`.

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
    public List<string> Validate(CreatePlayerRequest instance)
    {
        var errors = new List<string>();
        if (instance.Name.HasValue && instance.Name.Value.Length < 3)
            errors.Add("Name must be at least 3 characters.");
        return errors;
    }
}

[Dto(CreateValidator = typeof(MyCreateValidator))]
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

[Dto(CreateValidator = typeof(MyCreateValidator))]
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

## Requirements

- .NET 6+ (or .NET Framework with SDK-style projects and System.Text.Json NuGet)
- `required` keyword needs C# 11 / .NET 7+ (optional -- without it all properties are optional in Create)

## License

MIT
