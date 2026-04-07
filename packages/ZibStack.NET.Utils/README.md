# ZibStack.NET.Utils

A C# source generator that provides TypeScript-style utility types. No reflection, no runtime overhead.

## Install

```
dotnet add package ZibStack.NET.Utils
```

Requires `ZibStack.NET.Dto` for `PatchField<T>` (used in generated properties).

## What's included

### `[PartialFrom(typeof(T))]`

Like TypeScript's `Partial<T>` — generates a class where every property is a `PatchField<T>` with an `ApplyTo()` method:

```csharp
using ZibStack.NET.Utils;

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
- `ZibStack.NET.Dto` for `PatchField<T>` support

## License

MIT
