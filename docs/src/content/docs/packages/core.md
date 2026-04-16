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

### `[Destructurable<TSource>]`

Brings JS-style `{ picked, ...rest }` destructuring to C# — both sides typed end-to-end.
You declare a partial record describing the **shape** you want to pick; the generator
fills it with a `Split(source)` factory and a nested `Rest` record holding the complement.

```csharp
using ZibStack.NET.Core;

// Source — plain record, no attributes here.
public record Person(string Name, int Id, string Email, int Age, string City);

// Shape — primary-ctor record listing the picked properties.
[Destructurable<Person>]
public partial record PersonNameId(string Name, int Id);
```

The generator emits onto `PersonNameId`:

```csharp
public partial record PersonNameId
{
    public sealed record Rest(string Email, int Age, string City);

    public static PersonNameId FromSource(Person src) => new(src.Name, src.Id);
    public static Rest         RestOf(Person src)     => new(src.Email, src.Age, src.City);
    public static (PersonNameId Picked, Rest Remaining) Split(Person src)
        => (FromSource(src), RestOf(src));
}
```

Usage:

```csharp
var person = new Person("Alice", 42, "a@b.c", 30, "Warsaw");

var (picked, rest) = PersonNameId.Split(person);

picked.Name        // "Alice"  — typed
picked.Id          // 42       — typed
rest.Email         // "a@b.c"  — typed, IDE-autocompleted
rest.Age           // 30       — typed
rest.City          // "Warsaw" — typed
```

**Why a shape record (and not a lambda or method-name encoding)?** Anonymous types in C#
are nominal, not structural — they have no source-writable name a generator can emit code
against. The C# language team has explicitly declined both
[anonymous-type deconstruction](https://github.com/dotnet/csharplang/discussions/244) and
[spread/rest object syntax](https://github.com/dotnet/csharplang/discussions/7507),
positioning property mapping as "a job for a library method." Library methods, in turn,
need a *named* type to hand back as the rest container — that's what the shape record is.

The upside: every shape is also a regular DTO. Reuse it in responses, log payloads,
mappers. No throwaway anon, no `dynamic`, no untyped dictionary.

**Two declaration styles supported.** Primary-ctor records use positional construction;
body-style records fall back to object initializers:

```csharp
// Primary-ctor — generator emits `new(src.Name, src.Id)`
[Destructurable<Person>]
public partial record PersonNameId(string Name, int Id);

// Body — generator emits `new() { Name = src.Name, Email = src.Email }`
[Destructurable<Person>]
public partial record PersonContact
{
    public required string Name  { get; init; }
    public required string Email { get; init; }
}
```

**Diagnostics.** Property name and type are validated against the source at compile time:

| ID | Severity | Trigger |
|---|---|---|
| `ZDS0001` | Error   | Shape property does not exist on the source type |
| `ZDS0002` | Error   | Shape property's type does not match source's declared type |
| `ZDS0003` | Warning | Shape carries `[Destructurable<>]` but isn't `partial` (nothing emitted) |

Renames are caught at the next build — no chance of silent drift between shape and source.

### Pattern matching on the `Split` tuple

`Split(src)` returns a regular `ValueTuple<TPicked, TRest>`, so it plugs into the full
C# pattern-matching grammar — positional, property, switch with `when` guards.

```csharp
// Positional pattern — match the whole shape in one go
if (PersonNameId.Split(person) is ({ Name: "Admin", Id: 0 }, _))
{
    // newly created admin
}

// Descend into rest via a property pattern
if (PersonNameId.Split(person) is (var p, { Age: < 18 }))
{
    Console.WriteLine($"underage: {p.Name}");
}

// switch with when-guards — combine picked + rest constraints freely
var label = PersonNameId.Split(person) switch
{
    ({ Name: var n }, _)               when n.StartsWith("Guest") => $"guest: {n}",
    ({ Id: 0 }, _)                                                => "unsaved",
    (var p, { Age: < 18 })                                        => $"minor: {p.Name}",
    (var p, { Age: >= 65 })                                       => $"senior: {p.Name}",
    ({ Name: "Admin" }, { Email: var e })
        when e.EndsWith("@company.com")                           => $"internal admin ({e})",
    (var p, var rest)
        when rest.Age > 18 && rest.City == rest.Email.Split('@')[1] => $"{p.Name} emails from home",
    (var p, _)                                                    => $"regular: {p.Name}",
};
```

**Why this matters if you come from TypeScript:** `const { name, id, ...rest } = person`
in TS gives you `rest` as a plain object, and you reach into it with `rest.age`. The
shape-record approach gives you the same destructured shape (`picked` + typed `rest`)
plus a named, reusable type for both halves. Where TS bakes the shape into the
destructuring expression, C# moves it one level up into a partial record — the cost is
one declaration line, the win is that the shape can be reused outside the destructure.

## Requirements

- .NET 6+ (or .NET Framework with SDK-style projects)
- `ZibStack.NET.Dto` for `PatchField<T>` support (utility types only)

## License

MIT
