---
title: ZibStack.NET.Dto
description: A C# source generator that produces strongly-typed Create, Update, Response, and Query DTOs from domain models, with optional full CRUD API endpoint generation.
---

[![NuGet](https://img.shields.io/nuget/v/ZibStack.NET.Dto.svg)](https://www.nuget.org/packages/ZibStack.NET.Dto) [![Source](https://img.shields.io/badge/source-GitHub-blue)](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Dto)

A C# source generator that produces strongly-typed **Create**, **Update**, **Response**, and **Query** DTOs from your domain models, and optionally generates **full CRUD API endpoints** (Minimal API + MVC Controllers). No reflection, no runtime overhead. Supports generics, inheritance, nested types, flattening, validation propagation, and more.

Two configuration styles, **mixable per-class**:
- **Attribute markers** — `[CreateDto]`/`[UpdateDto]`/`[CrudApi]`/etc. for locality.
- **Fluent `IDtoConfigurator`** — central project-wide config with a typed builder. Useful for centralizing settings, configuring third-party types you can't annotate, or overriding settings without editing the model. See [Fluent configuration](#fluent-configuration-idtoconfigurator) below.

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


## Read more

This page covers the core mode + generated shape. Detailed reference and feature pages:

- [Attributes reference](/ZibStack.NET/packages/dto/attributes/) — every class- and property-level attribute, what it does, when to use it.
- [Fluent IDtoConfigurator](/ZibStack.NET/packages/dto/fluent-config/) — central project-wide config with a typed builder.
- [External types (`[CreateDtoFor]` / `[UpdateDtoFor]`)](/ZibStack.NET/packages/dto/external-types/) — DTOs for types you don't own.
- [Utility types](/ZibStack.NET/packages/dto/utility-types/) — Pick / Omit / Partial / Intersect from `ZibStack.NET.Core`.
- [Query DTO (`[QueryDto]`)](/ZibStack.NET/packages/dto/querydto/) — filter + sort + pagination DTOs.
- [`PaginatedResponse<T>`](/ZibStack.NET/packages/dto/paginated/) — the standard list wrapper.
- [CRUD API (`[CrudApi]`)](/ZibStack.NET/packages/dto/crud-api/) — full endpoint generation.
- [Response DTOs, mapping, `ApplyWithChanges`](/ZibStack.NET/packages/dto/response-mapping/) — `FromEntity` / `ProjectFrom`, nested + flatten, Diff, DtoMapper, Swagger.
- [JSON serializer & custom validation](/ZibStack.NET/packages/dto/json-and-validation/) — `PatchField` JSON registration + `IDtoValidator<T>`.

## Related guides

- [Full CRUD with SQLite](/ZibStack.NET/guides/crud-sqlite/)
- [Modeling Relationships & Query DSL](/ZibStack.NET/guides/relationships-query-dsl/)
- [PatchField Tri-State](/ZibStack.NET/guides/patchfield-tri-state/)

## Requirements

- .NET Standard 2.0+ (generator runs in any C# 12+ project)
- C# 12+ (`required` keyword, primary constructors)

## License

MIT.
