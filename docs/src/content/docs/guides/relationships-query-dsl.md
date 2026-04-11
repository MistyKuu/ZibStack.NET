---
title: Modeling Relationships & Query DSL
description: Deep dive into [OneToOne] / [OneToMany] and the ZibStack.NET.Query filter/sort DSL — with a concrete multi-entity schema, every operator by example, and how it translates to SQL.
---

This guide builds on [Full CRUD with SQLite](/ZibStack.NET/guides/crud-sqlite/) and focuses on the two topics that guide only touches lightly: **modeling relationships** with `[OneToOne]` / `[OneToMany]`, and **every operator in the filter DSL** with a concrete worked example.

You should read the CRUD guide first if you haven't — the project layout, `AddAop()` / `UseAop()` wiring, and `[CrudApi]` basics are assumed here.

## The schema

A small multi-entity domain that exercises every relationship shape. All four models are under `Models/` and are marked `[CrudApi]` so they get DTOs + endpoints + query DTOs automatically.

```csharp
// Models/Team.cs
using ZibStack.NET.Core;
using ZibStack.NET.Dto;

[CrudApi]
public partial class Team
{
    [DtoIgnore] public int Id { get; set; }
    public required string Name { get; set; }
    public required string City { get; set; }
    public int Founded { get; set; }

    [OneToMany]
    public ICollection<Player> Players { get; set; } = new List<Player>();
}
```

```csharp
// Models/Player.cs
using ZibStack.NET.Core;
using ZibStack.NET.Dto;

[CrudApi]
public partial class Player
{
    [DtoIgnore] public int Id { get; set; }
    public required string Name { get; set; }
    public int Level { get; set; }
    public string? Email { get; set; }

    public int? TeamId { get; set; }
    [OneToOne]
    public Team? Team { get; set; }

    [OneToMany]
    public ICollection<Achievement> Achievements { get; set; } = new List<Achievement>();
}
```

```csharp
// Models/Achievement.cs
using ZibStack.NET.Core;
using ZibStack.NET.Dto;

[CrudApi]
public partial class Achievement
{
    [DtoIgnore] public int Id { get; set; }
    public required string Title { get; set; }
    public int Points { get; set; }
    public DateTime EarnedAt { get; set; }

    public int PlayerId { get; set; }
    [OneToOne]
    public Player? Player { get; set; }
}
```

```csharp
// Models/Tournament.cs
using ZibStack.NET.Core;
using ZibStack.NET.Dto;

[CrudApi]
public partial class Tournament
{
    [DtoIgnore] public int Id { get; set; }
    public required string Name { get; set; }
    public DateTime StartsAt { get; set; }

    [OneToMany]
    public ICollection<Team> Teams { get; set; } = new List<Team>();
}
```

## Why these attributes exist

The relationship attributes are **compile-time markers**. They do nothing at runtime, carry no metadata through reflection, and have no EF Core dependency themselves. Their only job is to tell the source generators "this property is a navigation — expand it".

Concretely, when you decorate `Player.Team` with `[OneToOne]`, the Dto generator emits these lines **inside** the generated `PlayerQuery.ApplyFilter`:

```csharp
return FilterApplier.ApplyTree(query, tree, fieldName => fieldName switch
{
    "Name"       => (x => x.Name, typeof(string)),
    "Level"      => (x => x.Level, typeof(int)),
    "TeamId"     => (x => x.TeamId, typeof(int?)),
    "Team.Id"    => (x => x.Team!.Id, typeof(int)),        // ← added by [OneToOne]
    "Team.Name"  => (x => x.Team!.Name, typeof(string)),   // ← added by [OneToOne]
    "Team.City"  => (x => x.Team!.City, typeof(string)),   // ← added by [OneToOne]
    "Team.Founded" => (x => x.Team!.Founded, typeof(int)), // ← added by [OneToOne]
    _            => null
});
```

**Without the marker** the generator has no way to know if `Team` is a navigation or a nested value object, so it skips the property entirely and `filter=Team.Name=...` silently returns "unknown field". **With** the marker the navigation becomes a first-class filter path, and EF Core translates `x => x.Team.Name == "Warriors"` into an `INNER JOIN Teams` in the generated SQL.

The same applies for `[OneToMany]`, but with collection-aware predicates: `filter=Players.Name=*ski` becomes `x => x.Players.Any(p => EF.Functions.Like(p.Name, "%ski%"))`.

## `[OneToOne]` — the forward direction

`[OneToOne]` goes on the **navigation property** (the reference to the related entity), not the foreign key. The FK stays a plain `int?`:

```csharp
public int? TeamId { get; set; }     // plain FK — flat filter field
[OneToOne]
public Team? Team { get; set; }      // navigation — dot-notation filter fields
```

What this enables:

| Client usage | What it does | SQL |
|---|---|---|
| `filter=TeamId=1` | Flat FK filter (always works, even without `[OneToOne]`) | `WHERE TeamId = 1` |
| `filter=Team.Name=Warriors` | Join + filter on the related table | `INNER JOIN Teams ON … WHERE Teams.Name = 'Warriors'` |
| `sort=Team.City` | Sort parent rows by a child column | `ORDER BY Teams.City` |
| `select=Name,Team.Name` | Project a dotted field into the response | generated LINQ projection |

**FK convention.** The generator assumes `{NavProp}Id` (so `Team` → `TeamId`). Override with `[OneToOne(ForeignKey = "OwnerTeamId")]` if your naming is different.

**One navigation, both ends.** If you want filtering from `Team` back into `Player` too, also mark the reverse side with `[OneToMany]` on `Team.Players` (which we did in the schema above).

## `[OneToMany]` — the reverse direction

`[OneToMany]` goes on the **collection property** on the parent side. Filter and sort use collection predicates:

| Client usage | What it does | EF Core translation |
|---|---|---|
| `filter=Players.Count>5` | Teams with more than 5 players | `WHERE (SELECT COUNT(*) FROM Players p WHERE p.TeamId = t.Id) > 5` |
| `filter=Players.Level>80` | Teams where **any** player has level > 80 | `WHERE EXISTS (SELECT 1 FROM Players p WHERE p.TeamId = t.Id AND p.Level > 80)` |
| `filter=Players.Name=*ski` | Teams containing any player named *ski | `WHERE EXISTS (SELECT 1 FROM Players p WHERE p.TeamId = t.Id AND p.Name LIKE '%ski%')` |

The semantics for collection predicates is always **"any"** (exists-quantifier). There's no "all" variant in the current DSL because `filter=Players.Level>80` meaning "every player has level > 80" is almost never what clients actually want — the "any" form covers 95% of real queries.

**Why `Players.Count` is special.** `Count` isn't a property on `Player`; the DSL parser recognizes it as a collection aggregate and the Dto generator emits a dedicated predicate for it. If you have a property *literally named* `Count` on the child entity, the parser still treats it as the aggregate — rename it to avoid ambiguity.

## Chained navigations

`[OneToOne]` **does not** recurse. `filter=Team.City=LA` works, but `filter=Team.Country.Name=USA` doesn't — the generator only expands one level deep.

This is deliberate: arbitrary-depth expansion explodes the allowlist combinatorially (a 5-deep chain through 3 properties per step yields 243 paths per entity), and more importantly, deep chains are a strong smell that your API is exposing too much of your domain model. If you genuinely need `Team.Country.Name`, create a flattened column on `Team` (`TeamCountry`) or expose a dedicated endpoint that joins the right aggregate.

## The filter DSL — every operator

The parser is [`FilterParser`](https://github.com/MistyKuu/ZibStack.NET/blob/master/packages/ZibStack.NET.Query/src/ZibStack.NET.Query/FilterParser.cs) and produces an AST (`FilterAnd` / `FilterOr` / `FilterLeaf`) that the Dto-generated code maps onto strongly-typed `Expression<Func<T, bool>>` predicates. Everything below compiles down to LINQ → SQL without runtime reflection.

### Comparison operators

| Operator | Token | Example | SQL |
|---|---|---|---|
| Equals | `=` | `Level=50` | `Level = 50` |
| NotEquals | `!=` | `Level!=0` | `Level <> 0` |
| GreaterThan | `>` | `Level>50` | `Level > 50` |
| GreaterThanOrEqual | `>=` | `Level>=50` | `Level >= 50` |
| LessThan | `<` | `Level<100` | `Level < 100` |
| LessThanOrEqual | `<=` | `Level<=100` | `Level <= 100` |

### String match operators

| Operator | Token | Example | SQL |
|---|---|---|---|
| Contains | `=*` | `Name=*ski` | `Name LIKE '%ski%'` |
| NotContains | `!*` | `Name!*test` | `Name NOT LIKE '%test%'` |
| StartsWith | `^` | `Name^Ko` | `Name LIKE 'Ko%'` |
| NotStartsWith | `!^` | `Name!^Temp_` | `Name NOT LIKE 'Temp_%'` |
| EndsWith | `$` | `Email$@test.com` | `Email LIKE '%@test.com'` |
| NotEndsWith | `!$` | `Email!$@spam.com` | `Email NOT LIKE '%@spam.com'` |

### Set operators

| Operator | Token | Example | SQL |
|---|---|---|---|
| In | `=in=` | `Name=in=Alice;Bob;Eve` | `Name IN ('Alice', 'Bob', 'Eve')` |
| NotIn | `=out=` | `Level=out=0;1;2` | `Level NOT IN (0, 1, 2)` |

### Logic, grouping, modifiers

| Feature | Syntax | Example |
|---|---|---|
| AND | `,` | `Level>20,Level<80` |
| OR | `\|` | `Level<10\|Level>90` |
| Grouping | `()` | `(Level<10\|Level>90),Team.City=LA` |
| Case insensitive | `/i` (trailing) | `Name=*jan/i` |

Precedence: `()` > `,` (AND) > `|` (OR). So `A,B|C` parses as `(A AND B) OR C`. Add explicit grouping if you mean `A AND (B OR C)`: `A,(B|C)`.

## Worked examples

Given the seed data from the CRUD guide (Warriors, Lakers, Knicks + players and achievements), here's how every operator behaves:

### Flat filtering on a single entity

```bash
# Every player between level 20 and 60
curl 'http://localhost:5000/api/players?filter=Level>=20,Level<=60'

# Players whose email ends with @test.com (case insensitive)
curl 'http://localhost:5000/api/players?filter=Email$@test.com/i'

# Players named in a set
curl 'http://localhost:5000/api/players?filter=Name=in=Alice;Bob;Diana'

# Either low or high level, no middle
curl 'http://localhost:5000/api/players?filter=(Level<15|Level>50)'
```

### Dot notation via `[OneToOne]`

```bash
# Players on the Warriors
curl 'http://localhost:5000/api/players?filter=Team.Name=Warriors'

# Players whose team is based in a city starting with "L" (Lakers)
curl 'http://localhost:5000/api/players?filter=Team.City^L'

# Players above level 30 sorted by team name then descending level
curl 'http://localhost:5000/api/players?filter=Level>30&sort=Team.Name,-Level'
```

### Collection predicates via `[OneToMany]`

```bash
# Teams that have at least one high-level player
curl 'http://localhost:5000/api/teams?filter=Players.Level>=70'

# Teams with a player whose email ends in @test.com
curl 'http://localhost:5000/api/teams?filter=Players.Email$@test.com'

# Teams with more than 1 player
curl 'http://localhost:5000/api/teams?filter=Players.Count>1'

# Two-level drill — players with at least one achievement worth > 100 points
curl 'http://localhost:5000/api/players?filter=Achievements.Points>100'
```

### Sorting

```bash
# Single field, descending
curl 'http://localhost:5000/api/players?sort=-Level'

# Multi-field — primary by team name ascending, tie-break by level descending
curl 'http://localhost:5000/api/players?sort=Team.Name,-Level'

# Explicit direction keywords also work
curl 'http://localhost:5000/api/players?sort=Level desc,Name asc'
```

### Projection & count

```bash
# Only return the fields we care about
curl 'http://localhost:5000/api/players?select=Name,Level,Team.Name'

# Don't return rows — only the count
curl 'http://localhost:5000/api/players?filter=Level>30&count=true'
# → { "count": 3 }
```

## How the allowlist protects you

The generated `PlayerQuery.ApplyFilter` contains a `switch` statement mapping field names to predicates:

```csharp
fieldName => fieldName switch
{
    "Name"       => (x => x.Name, typeof(string)),
    "Level"      => (x => x.Level, typeof(int)),
    "Team.Name"  => (x => x.Team!.Name, typeof(string)),
    // …
    _            => null   // ← anything not in the list is rejected
}
```

Three consequences:

1. **Private fields are invisible.** Properties marked `[DtoIgnore]` or `[QueryIgnore]` never appear in the switch, so `filter=Password=*` returns "unknown field".
2. **Typos become 400s, not crashes.** `filter=Levle>10` hits the `_ => null` branch and the Query runtime returns `ArgumentException: Unknown field 'Levle'`, which the Dto-generated endpoint converts into a `400 Bad Request` with ProblemDetails. Always with an explicit field name so the client knows exactly what they typed wrong.
3. **AOT-safe.** No `typeof(T).GetProperty(fieldName)` at runtime. The entire field→predicate mapping is baked into the generated code at compile time, which means AOT publishing keeps working.

## When you *don't* want the generated allowlist

Sometimes you need a filter field that isn't a simple property — a computed value, a joined aggregate, or a custom alias. In those cases, skip `[CrudApi]`'s auto-generated query DTO and write one by hand that inherits from the generated one or uses Query directly:

```csharp
using ZibStack.NET.Query;

public static class PlayerQueryExtensions
{
    public static IQueryable<Player> ApplyCustomFilter(
        this IQueryable<Player> source,
        string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return source;
        var tree = FilterParser.ParseExpression(filter);
        return FilterApplier.ApplyTree(source, tree, fieldName => fieldName switch
        {
            "Name"                => (Expression<Func<Player, string>>)(p => p.Name), typeof(string),
            "AchievementPoints"   => ((Expression<Func<Player, int>>)(p => p.Achievements.Sum(a => a.Points))), typeof(int),
            _                     => null
        });
    }
}
```

This is an escape hatch — for everything else the generated DTO is what you want.

## Related reference

- [Core → Relationship Attributes](/ZibStack.NET/packages/core/#relationship-attributes) — full attribute reference with all properties
- [Query — Filter & Sort DSL](/ZibStack.NET/packages/query/) — operator table and DSL syntax
- [Dto → QueryDto](/ZibStack.NET/packages/dto/#query--filter-dto-querydto) — generated code reference
