---
title: ZibStack.NET.Query
description: Filter/sort DSL for REST APIs — parses query strings into LINQ expressions that translate to SQL. Compile-time field allowlists via source generation.
---

[![NuGet](https://img.shields.io/nuget/v/ZibStack.NET.Query.svg)](https://www.nuget.org/packages/ZibStack.NET.Query) [![Source](https://img.shields.io/badge/source-GitHub-blue)](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Query)

Filter/sort DSL for REST APIs. Parses query strings into LINQ expression trees that translate to SQL via EF Core. Source generation provides compile-time field allowlists — zero reflection, no unsafe field access.

## Install

```
dotnet add package ZibStack.NET.Query
```

When used with `ZibStack.NET.Dto`, the Dto source generator auto-detects `ZibStack.NET.Query` and adds `filter`/`sort` string parameters to all CRUD list endpoints.

## Quick Start

The standalone way to get a query DSL on any model is `[QueryDto]` from `ZibStack.NET.Dto`. As long as `ZibStack.NET.Query` is referenced in the same project, the Dto source generator automatically wires filter/sort string parsing into the generated query record:

```csharp
[QueryDto(DefaultSort = "Name")]
public partial class Player
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public string? Email { get; set; }
    public int? TeamId { get; set; }

    [OneToOne]
    public Team? Team { get; set; }
}
```

Generates `PlayerQuery` with `ApplyFilter(query, filter?)`, `ApplySort(query, sort?)`, `Apply(query, filter?, sort?)`, and `ProjectFields()`. Point it at an `IQueryable<Player>` in any endpoint:

```csharp
app.MapGet("/api/players", (string? filter, string? sort, AppDbContext db) =>
{
    var q = new PlayerQuery();
    return q.Apply(db.Players, filter, sort).ToList();
});
```

And the endpoint now accepts filter/sort strings:

```
GET /api/players?filter=Level>25,Name=*ski&sort=-Level
GET /api/players?filter=Team.Name=Lakers
GET /api/players?filter=(Level>50|Level<10),Team.City=LA
GET /api/players?filter=Name=in=Jan;Anna;Kasia
```

> **About `Team.Name` filtering** — dot notation across navigation properties only works because `Team` is marked with `[OneToOne]` from `ZibStack.NET.Core`. The Dto generator reads the relationship attribute and expands the navigation into the filter allowlist (`Team.Name`, `Team.City`, etc.) so the query DSL can reach into the related entity. Collection navigations use `[OneToMany]` to enable `filter=Players.Name=*ski` / `filter=Players.Count>5`. See [Core → Relationship Attributes](/ZibStack.NET/packages/core/#relationship-attributes) for the full contract.

> If you're using `[CrudApi]` (or the full `[ImTiredOfCrud]` from `ZibStack.NET.UI`), the generator produces the query record, the list endpoint, **and** wires `filter`/`sort`/`page`/`pageSize` query string parameters automatically — you don't have to write the endpoint yourself. `[QueryDto]` is what you reach for when you only want the DSL on a plain model without the rest of the CRUD scaffolding.

## Filter Operators

| Operator | Token | Example | SQL |
|---|---|---|---|
| Equals | `=` | `Name=Jan` | `WHERE Name = 'Jan'` |
| NotEquals | `!=` | `Role!=Admin` | `WHERE Role != 'Admin'` |
| GreaterThan | `>` | `Level>30` | `WHERE Level > 30` |
| GreaterThanOrEqual | `>=` | `Level>=30` | `WHERE Level >= 30` |
| LessThan | `<` | `Level<50` | `WHERE Level < 50` |
| LessThanOrEqual | `<=` | `Level<=50` | `WHERE Level <= 50` |
| Contains | `=*` | `Name=*ski` | `WHERE Name LIKE '%ski%'` |
| NotContains | `!*` | `Name!*test` | `WHERE Name NOT LIKE '%test%'` |
| StartsWith | `^` | `Name^Jan` | `WHERE Name LIKE 'Jan%'` |
| NotStartsWith | `!^` | `Name!^Jan` | `WHERE Name NOT LIKE 'Jan%'` |
| EndsWith | `$` | `Name$ski` | `WHERE Name LIKE '%ski'` |
| NotEndsWith | `!$` | `Name!$ski` | `WHERE Name NOT LIKE '%ski'` |
| In | `=in=` | `Name=in=Jan;Anna` | `WHERE Name IN ('Jan','Anna')` |
| NotIn | `=out=` | `Level=out=10;20` | `WHERE Level NOT IN (10,20)` |

## Logic & Modifiers

| Feature | Syntax | Example |
|---|---|---|
| AND | `,` | `Level>20,Level<50` |
| OR | `\|` | `Level>50\|Level<10` |
| Grouping | `()` | `(Level>50\|Level<10),Name=*ski` |
| Case insensitive | `/i` | `Name=jan/i` |
| Dot notation | `Nav.Field` | `Team.Name=Lakers` |

**Precedence:** `()` > `,` (AND) > `|` (OR)

## Sorting

```
GET /api/players?sort=-Level           # descending
GET /api/players?sort=Name             # ascending
GET /api/players?sort=Name desc        # explicit direction
GET /api/players?sort=-Level,Name      # multi-field
GET /api/players?sort=Team.Name        # sort by relation
```

## Collection Filtering (OneToMany)

Filter by child collection properties using Any, All, or Count:

```
GET /api/teams?filter=Players.Name=*ski              # Any player name contains "ski" (default)
GET /api/teams?filter=Players.Any.Name=*ski           # Same — explicit Any
GET /api/teams?filter=Players.All.Level>50            # ALL players have Level > 50
GET /api/teams?filter=Players.Count>5                 # Team has more than 5 players
GET /api/teams?filter=Players.Count=0                 # Teams with no players
```

Syntax: `Collection.Property` (implicit Any), `Collection.Any.Property`, `Collection.All.Property`, `Collection.Count`.
EF Core translates to EXISTS/NOT EXISTS/COUNT subqueries.

## Relation Filtering (Dot Notation)

When your model has navigation properties with `[OneToOne]` (from `ZibStack.NET.Core`), the generator automatically adds dot-notation paths to the filter allowlist:

```csharp
public class Player
{
    public int? TeamId { get; set; }

    [OneToOne]
    public Team? Team { get; set; }  // enables Team.Name, Team.City, etc.
}
```

```
GET /api/players?filter=Team.Name=Lakers          →  LEFT JOIN Teams ... WHERE t.Name = 'Lakers'
GET /api/players?filter=Team.City=Boston,Level>30  →  JOIN + compound WHERE
GET /api/players?sort=-Team.Name                   →  JOIN + ORDER BY
```

EF Core translates `x => x.Team.Name` into a SQL JOIN automatically.

## Field Projection (select=)

Return only specific fields to reduce payload:

```
GET /api/players?select=Name,Level                        # flat fields only
GET /api/players?select=Name,Level,Team.Name              # include relation fields
GET /api/players?select=Name,Level,Team.Name,Team.City    # multiple relation fields
```

Response: `{ "name": "Jan", "level": 42, "team": { "name": "Lakers", "city": "LA" } }`

## Standalone Count

Get count without fetching data:

```
GET /api/players?filter=Level>25&count=true    # → { "count": 42 }
GET /api/players?count=true                     # → { "count": 150 }
```

## How Source Generation Helps

The Dto generator produces a **compile-time field allowlist** per entity:

```csharp
// Auto-generated (you never write this):
clause.Field.ToLowerInvariant() switch
{
    "name"      => FilterApplier.BuildPredicate<Player, string>(x => x.Name, clause),
    "level"     => FilterApplier.BuildPredicate<Player, int>(x => x.Level, clause),
    "team.name" => FilterApplier.BuildPredicate<Player, string>(x => x.Team.Name, clause),
    _ => null,  // unknown fields silently ignored
};
```

This means:
- **No reflection** — static switch, not runtime property lookup
- **Security** — `[DtoIgnore]`, `[QueryIgnore]`, `[UiTableColumn(Filterable=false)]` exclude fields from the allowlist. Sensitive fields like `Password` never appear.
- **Type safety** — each field has its correct C# type at compile time
- **AOT compatible** — no `Type.GetProperty()` or expression compilation at runtime

## [QueryDto] Attribute

Standalone query DSL without CRUD endpoints — use on any model:

```csharp
[QueryDto(DefaultSort = "Name")]
public partial class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }

    [OneToOne]
    public Category? Category { get; set; }

    [OneToMany]
    public ICollection<Tag> Tags { get; set; }
}
```

Generates `ProductQuery` with `ApplyFilter(query, filter?)`, `ApplySort(query, sort?)`, `Apply(query, filter?, sort?)`, `ProjectFields()`.
`Sortable` defaults to `true` — set `[QueryDto(Sortable = false)]` for endpoints with a fixed result order (analytics, exports).

## Standalone Usage

You can use the parser and applier directly without the Dto source generator:

```csharp
using ZibStack.NET.Query;

var q = new ProductQuery();
var query = dbContext.Products.AsQueryable();

// DSL approach:
query = q.Apply(query, "Price>100,Category.Name=Electronics", "-Price");

// Typed approach (when no DSL string):
var q2 = new ProductQuery { Name = "laptop", SortBy = "Price" };
query = q2.Apply(query);
```

## Supported Types

The filter applier handles: `string`, `int`, `long`, `decimal`, `double`, `float`, `bool`, `DateTime`, `DateTimeOffset`, `Guid`, enums, and their nullable variants.

## Requirements

- .NET 8+ (runtime library targets netstandard2.0 and net8.0)
- `ZibStack.NET.Dto` for auto-generated endpoint integration (optional — standalone usage works without it)

## License

MIT
