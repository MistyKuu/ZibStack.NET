---
title: Utility types
description: TypeScript-style Pick / Omit / Partial / Intersect generators from the ZibStack.NET.Core companion package.
---

## Utility types (from `ZibStack.NET.Core`)

TypeScript-style utility types are available in the separate [`ZibStack.NET.Core`](/ZibStack.NET/packages/core/) package:

```csharp
using ZibStack.NET.Core;

[PartialFrom(typeof(Player))]       // all properties optional + ApplyTo()
public partial record PartialPlayer;

[PickFrom(typeof(Player), "Name", "Level")]  // whitelist
public partial record PlayerSummary;

[OmitFrom(typeof(Player), "Id", "CreatedAt")]  // blacklist
public partial record PlayerWithoutMeta;

[IntersectFrom(typeof(Player))]     // combine multiple types
[IntersectFrom(typeof(Address))]
public partial record PlayerWithAddress;
```

See the [ZibStack.NET.Core documentation](/ZibStack.NET/packages/core/) for full documentation.

