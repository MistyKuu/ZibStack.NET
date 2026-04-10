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

Requires `ZibStack.NET.Dto` for `PatchField<T>` (used in generated utility type properties).

## Relationship Attributes

### `[OneToOne]`

Declares a one-to-one navigation property. Used by the Dto generator for dot-notation filtering and by the UI generator for EF Core configuration.

```csharp
using ZibStack.NET.Core;

public class Player
{
    public int TeamId { get; set; }

    [OneToOne]
    public Team? Team { get; set; }
}
```

### `[OneToMany]`

Declares a one-to-many relationship on a collection navigation property.

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
- `Label` — display label for UI
- `SchemaUrl` / `FormSchemaUrl` — override URLs for UI schema resolution

### `[Entity]`

Opt-in for EF Core `IEntityTypeConfiguration<T>` generation.

```csharp
[Entity(TableName = "Players", Schema = "dbo")]
public partial class Player { ... }
```


Defines a child table relationship for hierarchical drill-down (ERP-style).

```csharp
public partial class VoivodeshipView { ... }
```

## Utility Type Attributes

### `[PartialFrom(typeof(T))]`

Like TypeScript's `Partial<T>` — generates a class where every property is a `PatchField<T>` with an `ApplyTo()` method:

```csharp
using ZibStack.NET.Core;

[PartialFrom(typeof(Player))]
public partial record PartialPlayer;

// Generated: PatchField properties for all public properties of Player + ApplyTo(Player)
```

### `[IntersectFrom(typeof(T))]`

Like TypeScript's `&` operator — combine properties from multiple types:

```csharp
[IntersectFrom(typeof(Player))]
[IntersectFrom(typeof(Address))]
public partial record PlayerWithAddress;

// Generated: all properties from both types + ApplyTo(Player) + ApplyTo(Address)
```

### `[PickFrom(typeof(T), ...)]`

Like TypeScript's `Pick<T, K>` — whitelist of properties:

```csharp
[PickFrom(typeof(Player), nameof(Player.Name), nameof(Player.Level))]
public partial record PlayerSummary;
```

### `[OmitFrom(typeof(T), ...)]`

Like TypeScript's `Omit<T, K>` — exclude listed properties:

```csharp
[OmitFrom(typeof(Player), nameof(Player.Id), nameof(Player.CreatedAt))]
public partial record PlayerWithoutMeta;
```

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

## Requirements

- .NET 6+ (or .NET Framework with SDK-style projects)
- `ZibStack.NET.Dto` for `PatchField<T>` support (utility types only)

## License

MIT
