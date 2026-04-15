---
title: "Query DTO ([QueryDto])"
description: Generated filter + sort + pagination DTOs with ApplyFilter / ApplySort / Apply(IQueryable). Optional ZibStack.NET.Query DSL integration.
---

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

