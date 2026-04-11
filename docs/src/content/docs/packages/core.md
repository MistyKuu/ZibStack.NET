---
title: ZibStack.NET.Core
description: Source generator for shared attributes — relationships, entity configuration, TypeScript-style utility types, and JS-style destructuring. No reflection, no runtime overhead.
---

[![NuGet](https://img.shields.io/nuget/v/ZibStack.NET.Core.svg)](https://www.nuget.org/packages/ZibStack.NET.Core) [![Source](https://img.shields.io/badge/source-GitHub-blue)](https://github.com/MistyKuu/ZibStack.NET/tree/master/packages/ZibStack.NET.Core)

Source generator for shared attributes used across ZibStack.NET packages — relationships, entity configuration, and TypeScript-style utility types. No reflection, no runtime overhead.

> **Note:** This package replaces `ZibStack.NET.Utils`. The utility type attributes moved from the `ZibStack.NET.Utils` namespace to `ZibStack.NET.Core`. Relationship and entity attributes moved from `ZibStack.NET.UI` to `ZibStack.NET.Core`.

## Install

```
dotnet add package ZibStack.NET.Core
```

`[PartialFrom]` requires `ZibStack.NET.Dto` for `PatchField<T>`. The other utility type attributes (`[PickFrom]`, `[OmitFrom]`, `[IntersectFrom]`) emit plain properties and have no extra dependency.

## Relationship Attributes

These attributes live in `ZibStack.NET.Core` because they are read by **multiple generators** — keeping them in a single dependency-free package avoids cyclic references and duplicate declarations across `ZibStack.NET.Dto`, `ZibStack.NET.Query`, and `ZibStack.NET.UI`.

The attributes themselves are pure markers (no EF Core or ASP.NET runtime dependency). Each consuming generator picks up only the parts it cares about.

### `[OneToOne]`

Declares a one-to-one navigation property. Consumed by:

- **`ZibStack.NET.Dto` / `ZibStack.NET.Query`** — expands the navigation into the generated `QueryDto` filter allowlist so `filter=Team.Name=Lakers`, `sort=Team.City`, and `select=Team.Name` all work. Without this marker the Dto generator skips the navigation entirely (complex types aren't valid query parameters on their own), so relational filtering is silently unavailable.
- **`ZibStack.NET.UI`** — excludes the navigation from auto-generated forms/tables, registers it as a related entity for drill-down, and (when combined with `[Entity]`) emits the EF Core `HasOne().WithOne()` configuration.

```csharp
using ZibStack.NET.Core;

public class Player
{
    public int TeamId { get; set; }

    [OneToOne]
    public Team? Team { get; set; }
}
```

Properties:
- `ForeignKey` — foreign key property name on this type (auto-detected by the `{NavProp}Id` convention if omitted — e.g. `Team` → `TeamId`)
- `Label` — display label used by the UI generator
- `SchemaUrl` / `FormSchemaUrl` — override URLs for UI schema resolution

### `[OneToMany]`

Declares a one-to-many relationship on a collection navigation property. Consumed by:

- **`ZibStack.NET.Dto` / `ZibStack.NET.Query`** — lets the query DSL reach into the collection. `filter=Players.Name=*ski` translates to "any player named *ski", `filter=Players.Count>5` to "teams with more than 5 players". Without this marker the collection is invisible to the filter allowlist.
- **`ZibStack.NET.UI`** — emits a child table for hierarchical ERP-style drill-down on the parent's detail view, and (when combined with `[Entity]`) generates the EF Core `HasMany().WithOne()` configuration.

```csharp
public class Team
{
    public int Id { get; set; }
    public string Name { get; set; }

    [OneToMany(Label = "Players")]
    public ICollection<Player> Players { get; set; } = new List<Player>();
}
```

Properties:
- `ForeignKey` — foreign key property name on the child type (auto-detected by convention if omitted)
- `Label` — display label used by the UI generator for the child table tab
- `SchemaUrl` / `FormSchemaUrl` — override URLs for UI schema resolution

### `[Entity]`

Opt-in marker that tells the **`ZibStack.NET.UI`** generator to emit an EF Core `IEntityTypeConfiguration<T>` for this class — including table mapping, key configuration, and any `HasOne` / `HasMany` calls derived from `[OneToOne]` / `[OneToMany]` navigations on the same type.

The attribute itself has no dependency on `Microsoft.EntityFrameworkCore`; only the **generated** configuration class references EF Core, so consumers who don't use EF Core simply don't apply `[Entity]` and pay nothing.

```csharp
[Entity(TableName = "Players", Schema = "dbo")]
public partial class Player { ... }
```

Properties:
- `TableName` — overrides the database table name (defaults to the class name)
- `Schema` — database schema name

## Utility Type Attributes

### `[PartialFrom(typeof(T))]`

Like TypeScript's `Partial<T>` — every property of the target type becomes a `PatchField<T>` so you can model partial updates (the value can be **unset**, set to a value, or explicitly set to `null`). An `ApplyTo(target)` method writes only the fields that were actually provided. Used by Dto-style `Update*Request` shapes.

```csharp
using ZibStack.NET.Core;

[PartialFrom(typeof(Player))]
public partial record PartialPlayer;

// Generated:
//   public PatchField<string> Name { get; init; }
//   public PatchField<int>    Level { get; init; }
//   public PatchField<string?> Email { get; init; }
//   ...
//   public void ApplyTo(Player target) { /* writes only fields with HasValue */ }
```

> Requires `ZibStack.NET.Dto` for `PatchField<T>`. The other three utility-type attributes below do **not** use `PatchField` — they emit plain properties.

### `[PickFrom(typeof(T), ...)]`

Like TypeScript's `Pick<T, K>` — generates a record with the whitelisted properties **as plain fields** (their original types, not `PatchField`), plus a static `FromEntity(source)` factory that copies them from the source. Use it for projections / lightweight DTOs.

```csharp
[PickFrom(typeof(Player), nameof(Player.Name), nameof(Player.Level))]
public partial record PlayerSummary;

// Generated:
//   public string Name  { get; init; } = default!;
//   public int    Level { get; init; } = default!;
//   public static PlayerSummary FromEntity(Player source) => new() { Name = source.Name, Level = source.Level };

// Usage:
var summary = PlayerSummary.FromEntity(player);
```

### `[OmitFrom(typeof(T), ...)]`

Like TypeScript's `Omit<T, K>` — same as `PickFrom` but you list the properties to **exclude**. Also emits plain properties + `FromEntity(source)`.

```csharp
[OmitFrom(typeof(Player), nameof(Player.Id), nameof(Player.CreatedAt))]
public partial record PlayerWithoutMeta;

// Generated:
//   public string  Name  { get; init; } = default!;
//   public int     Level { get; init; } = default!;
//   public string? Email { get; init; } = default!;
//   public static PlayerWithoutMeta FromEntity(Player source) => new() { /* all included fields */ };
```

### `[IntersectFrom(typeof(T))]`

Like TypeScript's `&` operator — generates a record that **merges** properties from multiple sources (deduplicated by name; first source wins on conflict). Emits plain properties, plus a `FromEntity(source)` per source type and an `ApplyTo(target)` per source type.

```csharp
[IntersectFrom(typeof(Player))]
[IntersectFrom(typeof(Address))]
public partial record PlayerWithAddress;

// Generated:
//   public string Name   { get; init; } = default!;   // from Player
//   public int    Level  { get; init; } = default!;   // from Player
//   public string City   { get; init; } = default!;   // from Address
//   public string Street { get; init; } = default!;   // from Address
//
//   public static PlayerWithAddress FromEntity(Player source)  => new() { /* fills Player fields  */ };
//   public static PlayerWithAddress FromEntity(Address source) => new() { /* fills Address fields */ };
//   public void ApplyTo(Player target)  { /* writes Player  fields back */ }
//   public void ApplyTo(Address target) { /* writes Address fields back */ }

// Usage — chain FromEntity + with-expression to merge:
var combined = PlayerWithAddress.FromEntity(player) with
{
    City   = address.City,
    Street = address.Street,
};
combined.ApplyTo(somePlayer);   // writes Player-side fields back
combined.ApplyTo(someAddress);  // writes Address-side fields back
```

Unlike `[PartialFrom]`, none of `[PickFrom]`/`[OmitFrom]`/`[IntersectFrom]` use `PatchField` — they model **shape transformations**, not partial updates. If you want a partial-update DTO, use `[PartialFrom]` (or the `Update*Request` shapes generated by `[CrudApi]` in `ZibStack.NET.Dto`).

## JS-Style Destructuring

### `[Destructurable]`

Brings JavaScript-style destructuring with rest pattern to C#. Mark a type with `[Destructurable]`, then call any `PickXxx()` method where `Xxx` is the concatenation of property names — the source generator emits the matching extension method and a typed 'rest' object on demand.

```csharp
using ZibStack.NET.Core;

[Destructurable]
public partial class Person
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public int Age { get; set; }
    public string City { get; set; } = "";
}

// Usage — fully typed, with autocomplete and refactoring support:
var person = new Person { Name = "Alice", Id = 42, Email = "a@b.c", Age = 30, City = "Warsaw" };

// Single property pick
var (name, rest) = person.PickName();
// rest: PersonRest_Name { Id, Email, Age, City }

// Multi-property pick
var (name, id, rest) = person.PickNameId();
// rest: PersonRest_NameId { Email, Age, City }

// Three-property pick
var (name, id, email, rest) = person.PickNameIdEmail();
// rest: PersonRest_NameIdEmail { Age, City }
```

**How it works:** The source generator scans every `PickXxx()` invocation in your code. For each unique combination it finds, it emits:

1. An extension method on the source type returning `(T1, T2, ..., RestType)` tuple
2. A `TypeNameRest_<combo>` class containing the remaining properties

Only the combos you actually use are generated — no combinatorial explosion. With 5 properties you could theoretically produce 31 rest types, but if you only use 3 distinct picks, the generator emits just 3.

**Property name resolution:** Method names are parsed using greedy longest-match against the type's properties. So if `Person` has both `Name` and `NameId`, `PickNameId` matches the longer `NameId` property first; `PickNameIdEmail` is parsed as `NameId` + `Email`.

**Code map for IDE navigation:** When the source type is declared `partial`, the generator emits a partial type with an XML `<summary>` listing all generated picks via clickable `<see cref>` links:

```csharp
[Destructurable]
public partial class Person { ... }
//                  ^^^^^^^ — required for code map
```

Hover `Person` in your IDE → tooltip shows:

> **Destructurable code map for Person:**
> - Pick methods: `__Person_Destructurable`
> - `PickName(Person)` — picks Name, rest as `PersonRest_Name`
> - `PickNameId(Person)` — picks Name, Id, rest as `PersonRest_NameId`
> - `PickNameIdEmail(Person)` — picks Name, Id, Email, rest as `PersonRest_NameIdEmail`

Each link is clickable — F12 / Go To Definition jumps to the generated type. Without `partial`, the extension methods and rest types still work, but the code map is not emitted.

**Limits:**
- Each property can only be picked once per call (`PickNameName` is invalid)
- Picks are positional in the tuple — order in the method name determines order in the deconstruction (`PickNameId` returns `(Name, Id, rest)`, not `(Id, Name, rest)`)
- Property names that aren't valid C# identifier prefixes won't resolve

### Pattern matching with `PickXxx()`

`PickXxx()` returns a regular C# `ValueTuple`, which means it plugs straight into **positional patterns**, **property patterns**, and `switch` expressions with `when` guards. One thing to keep in mind before you start writing patterns:

> **Every `PickXxx()` produces exactly N+1 slots: one slot per picked property, then a single `rest` object at the end.** The `rest` is a strongly-typed object with properties for everything you didn't pick — not additional tuple elements. If you want to match on an unpicked field, go through the rest via a property pattern.

Concretely, for `Person { Name, Id, Email, Age, City }`:

```csharp
person.PickName()              // (string, PersonRest_Name)            — 2 slots
person.PickNameId()            // (string, int, PersonRest_NameId)     — 3 slots
person.PickNameIdEmail()       // (string, int, string, PersonRest_…)  — 4 slots

// rest is an object, NOT more tuple elements:
var (name, id, rest) = person.PickNameId();
// rest.Age, rest.Email, rest.City — access as properties
```

**Positional pattern in `if`** — match shape and values in one go:

```csharp
if (person.PickNameId() is ("Admin", 0, _))
{
    // Name == "Admin" AND Id == 0 — newly created admin
}
```

**Pattern matching the rest object** — use a nested property pattern in the last slot:

```csharp
// Match on a field that lives inside rest
if (person.PickNameId() is (var name, _, { Age: < 18 }))
{
    Console.WriteLine($"underage: {name}");
}

// Multiple rest fields at once
if (person.PickName() is (var name, { Age: >= 18 and < 65, City: "Warsaw" }))
{
    Console.WriteLine($"working-age warsaw local: {name}");
}
```

**`switch` expression with `when` guards** — full combination of positional + property + logical conditions:

```csharp
var description = person.PickNameId() switch
{
    // Guard on a picked slot (needs when because StartsWith is a method call)
    (var n, _, _)            when n.StartsWith("Guest")  => $"guest: {n}",

    // Constant pattern on slot 1
    (_, 0, _)                                             => "unsaved",

    // Drill into rest via property pattern
    (var n, _, { Age: < 18 })                             => $"minor: {n}",
    (var n, _, { Age: >= 65 })                            => $"senior: {n}",

    // Mix slot match with a rest-property guard
    ("Admin", var id, { Email: var e })
        when id > 0 && e.EndsWith("@company.com")         => $"internal admin ({e})",

    // Bind rest and use it in `when` — equivalent to a nested property pattern,
    // useful when the condition spans multiple rest fields
    (var n, _, var rest) when rest.Age > 18 && rest.City == rest.Email.Split('@')[1]
                                                            => $"{n} emails from home",

    // Fallback
    (var n, _, _)                                          => $"regular: {n}"
};
```

Two patterns to notice:
- `(var n, _, { Age: < 18 })` — the third slot isn't discarded, it's matched with a property pattern that descends into `rest.Age`. The compiler knows `rest` is `PersonRest_NameId` so `Age` is strongly typed.
- `(var n, _, var rest)` + `when` — bind the whole rest object, then use it freely in the guard. Useful when you need multiple rest fields in one boolean expression.

**Why this matters if you come from TypeScript:** `const { name, id, ...rest } = person` in TS gives you `rest` as a plain object, and you reach into it with `rest.age`. `PickXxx()` + positional pattern does exactly the same shape — picked fields are individual slots, everything else lives behind a typed `rest` handle. Positional patterns on `ValueTuple` happen to use parentheses `(...)` instead of square brackets `[...]`, but the mental model is identical.

## Requirements

- .NET 6+ (or .NET Framework with SDK-style projects)
- `ZibStack.NET.Dto` for `PatchField<T>` support (utility types only)

## License

MIT
