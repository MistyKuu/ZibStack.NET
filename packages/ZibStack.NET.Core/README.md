# ZibStack.NET.Core

Source generator for shared attributes and TypeScript-style utility types — used across ZibStack.NET packages for relationships, entity configuration, and type manipulation.

## Install

```
dotnet add package ZibStack.NET.Core
```

## Attributes

Core hosts marker attributes shared by multiple ZibStack.NET generators. They have no runtime dependencies — each consumer (`Dto`, `Query`, `UI`) reads only what it needs.

### Relationships

`[OneToOne]` and `[OneToMany]` are consumed by:
- **`Dto` / `Query`** — dot-notation filtering (`team.name`) and collection predicates (`Any` / `All` / `Count`).
- **`UI`** — excluded from auto-generated forms/tables, surfaced as ERP-style drill-down child tables, and (with `[Entity]`) emitted into EF Core configuration.

```csharp
public class Player
{
    public int TeamId { get; set; }

    [OneToOne]
    public Team? Team { get; set; }
}

public class Team
{
    public int Id { get; set; }
    public string Name { get; set; }

    [OneToMany(Label = "Players")]
    public ICollection<Player> Players { get; set; }
}
```

### Entity Configuration

`[Entity]` opts a class into EF Core `IEntityTypeConfiguration<T>` generation by the **`UI`** generator. The attribute itself does not reference `Microsoft.EntityFrameworkCore` — only the generated configuration class does, so non-EF consumers pay nothing.

```csharp
[Entity(TableName = "Players", Schema = "dbo")]
public partial class Player { ... }
```

### Utility Types

```csharp
// TypeScript Partial<T> — every property becomes PatchField<T> for partial updates;
// generated ApplyTo() writes only fields that were actually set.
// Requires ZibStack.NET.Dto for PatchField<T>.
[PartialFrom(typeof(Player))]
public partial class UpdatePlayer;

// TypeScript Pick<T, K> — plain properties + static FromEntity(source) projection.
[PickFrom(typeof(Player), nameof(Player.Name), nameof(Player.Level))]
public partial record PlayerSummary;
// Usage: var s = PlayerSummary.FromEntity(player);

// TypeScript Omit<T, K> — same as Pick but lists what to exclude.
[OmitFrom(typeof(Player), nameof(Player.Id), nameof(Player.CreatedAt))]
public partial record PlayerWithoutMeta;

// TypeScript intersection (&) — merge multiple sources. Plain properties +
// FromEntity per source + ApplyTo per source.
[IntersectFrom(typeof(Player))]
[IntersectFrom(typeof(Team))]
public partial record PlayerWithTeam;
// Usage:
//   var combined = PlayerWithTeam.FromEntity(player) with { Name = team.Name, ... };
//   combined.ApplyTo(somePlayer);
//   combined.ApplyTo(someTeam);
```

`PartialFrom` uses `PatchField<T>` because it models *partial updates* (semantics of TypeScript `Partial<T>` for PATCH endpoints). `Pick`/`Omit`/`Intersect` are pure **shape transformations** — they generate plain properties so the resulting record can be passed around like any other DTO without unwrapping `PatchField` values.

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/core/](https://mistykuu.github.io/ZibStack.NET/packages/core/)
