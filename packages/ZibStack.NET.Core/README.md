# ZibStack.NET.Core

Source generator for shared attributes and TypeScript-style utility types — used across ZibStack.NET packages for relationships, entity configuration, and type manipulation.

## Install

```
dotnet add package ZibStack.NET.Core
```

## Attributes

### Relationships

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

```csharp
[Entity(TableName = "Players", Schema = "dbo")]
public partial class Player { ... }

[ChildTable(typeof(TaskItem), ForeignKey = "ProjectId", Label = "Tasks")]
public partial class Project { ... }
```

### Utility Types

```csharp
// TypeScript Partial<T> — generates PatchField properties + ApplyTo method
[PartialFrom(typeof(Player))]
public partial class UpdatePlayer;

// TypeScript Pick<T, K> — only selected properties
[PickFrom(typeof(Player), nameof(Player.Name), nameof(Player.Level))]
public partial record PlayerSummary;

// TypeScript Omit<T, K> — all except listed
[OmitFrom(typeof(Player), nameof(Player.Id), nameof(Player.CreatedAt))]
public partial record PlayerWithoutMeta;

// TypeScript intersection (&) — combine multiple types
[IntersectFrom(typeof(Player))]
[IntersectFrom(typeof(Team))]
public partial record PlayerWithTeam;
```

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/core/](https://mistykuu.github.io/ZibStack.NET/packages/core/)
