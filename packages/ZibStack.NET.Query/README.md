# ZibStack.NET.Query

Filter/sort DSL for REST APIs — parses query strings into LINQ expressions that translate to SQL. Zero reflection, compile-time field allowlists via source generation.

## Install

```
dotnet add package ZibStack.NET.Query
```

When used with `ZibStack.NET.Dto`, the source generator auto-detects `ZibStack.NET.Query` and adds `filter`/`sort` string parameters to CRUD list endpoints.

## Quick Start

```csharp
// Your model — [CrudApi] or [ImTiredOfCrud] with ZibStack.NET.Dto
[ImTiredOfCrud(DefaultSort = "Name")]
public partial class Player
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Level { get; set; }
    public string? Email { get; set; }
    public int? TeamId { get; set; }

    [OneToOne]
    public Team? Team { get; set; }
}
```

That's it. The generated list endpoint now accepts `filter` and `sort` query string parameters:

```
GET /api/players?filter=Level>25,Name=*ski&sort=-Level&page=1&pageSize=20
GET /api/players?filter=Team.Name=Lakers                          # relation → auto JOIN
GET /api/players?filter=(Level>50|Level<10),Team.City=LA          # OR + grouping
GET /api/players?filter=Name=in=Jan;Anna;Kasia                    # IN list
```

## Filter Operators

| Operator | Token  | Example                | SQL                          |
|----------|--------|------------------------|------------------------------|
| Equals   | `=`    | `Name=Jan`             | `WHERE Name = 'Jan'`         |
| NotEquals| `!=`   | `Role!=Admin`          | `WHERE Role != 'Admin'`      |
| Greater  | `>`    | `Level>30`             | `WHERE Level > 30`           |
| GreaterEq| `>=`   | `Level>=30`            | `WHERE Level >= 30`          |
| Less     | `<`    | `Level<50`             | `WHERE Level < 50`           |
| LessEq   | `<=`   | `Level<=50`            | `WHERE Level <= 50`          |
| Contains | `=*`   | `Name=*ski`            | `WHERE Name LIKE '%ski%'`    |
| NotContains | `!*`| `Name!*test`           | `WHERE Name NOT LIKE '%t%'`  |
| StartsWith | `^`  | `Name^Jan`             | `WHERE Name LIKE 'Jan%'`     |
| NotStartsWith|`!^`| `Name!^Jan`            | `WHERE Name NOT LIKE 'Jan%'` |
| EndsWith | `$`    | `Name$ski`             | `WHERE Name LIKE '%ski'`     |
| NotEndsWith|`!$`  | `Name!$ski`            | `WHERE Name NOT LIKE '%ski'` |
| In       | `=in=` | `Name=in=Jan;Anna`     | `WHERE Name IN ('Jan','Anna')`|
| NotIn    | `=out=`| `Level=out=10;20`      | `WHERE Level NOT IN (10,20)` |

## Logic & Modifiers

| Feature        | Syntax         | Example                                |
|----------------|----------------|----------------------------------------|
| AND            | `,`            | `Level>20,Level<50`                    |
| OR             | `\|`           | `Level>50\|Level<10`                   |
| Grouping       | `()`           | `(Level>50\|Level<10),Name=*ski`       |
| Case insensitive | `/i`         | `Name=jan/i`                           |
| Dot notation   | `Nav.Field`    | `Team.Name=Lakers`                     |

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

## [ZQuery] Attribute

Standalone query DSL without CRUD endpoints — use on any model:

```csharp
[ZQuery(DefaultSort = "Name")]
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
`[ZQuery]` defaults `Sortable = true`. Alias for `[QueryDto]`.

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

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/query/](https://mistykuu.github.io/ZibStack.NET/packages/query/)
