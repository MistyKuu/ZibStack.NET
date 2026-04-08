# ZibStack.NET.Utils

Source generator providing TypeScript-style utility types (`Partial`, `Pick`, `Omit`, `Intersect`) for C# — no reflection, no runtime overhead.

## Install

```
dotnet add package ZibStack.NET.Utils
```

## Quick Start

```csharp
[PartialFrom(typeof(Player))]
public partial record PartialPlayer;
// Generated: PatchField properties for all Player properties + ApplyTo(Player)

[PickFrom(typeof(Player), nameof(Player.Name), nameof(Player.Level))]
public partial record PlayerSummary;

[OmitFrom(typeof(Player), nameof(Player.Id))]
public partial record PlayerWithoutId;
```

## Documentation

Full documentation: [mistykuu.github.io/ZibStack.NET/packages/utils/](https://mistykuu.github.io/ZibStack.NET/packages/utils/)
