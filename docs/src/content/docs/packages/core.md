---
title: ZibStack.NET.Core
description: Source generator for shared attributes — relationships, entity configuration, and TypeScript-style utility types. No reflection, no runtime overhead.
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

## Requirements

- .NET 6+ (or .NET Framework with SDK-style projects)
- `ZibStack.NET.Dto` for `PatchField<T>` support (utility types only)

## License

MIT
