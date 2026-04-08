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

## Standalone Usage

You can use the parser and applier directly without the Dto source generator:

```csharp
using ZibStack.NET.Query;

// Parse filter string → expression tree
var expr = FilterParser.ParseExpression("Level>25,Name=*ski");

// Apply to any IQueryable<T>
var filtered = FilterApplier.ApplyTree(dbContext.Players, expr, clause =>
    clause.Field.ToLowerInvariant() switch
    {
        "level" => FilterApplier.BuildPredicate<Player, int>(x => x.Level, clause),
        "name"  => FilterApplier.BuildPredicate<Player, string>(x => x.Name, clause),
        _ => null,
    });

// Parse and apply sort
var sorted = SortParser.Parse("-Level");
```

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/query/](https://mistykuu.github.io/ZibStack.NET/packages/query/)
