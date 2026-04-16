---
title: TypeGen — Polymorphism & interfaces
description: "[JsonPolymorphic] + [JsonDerivedType] → TS discriminated unions + OpenAPI oneOf + discriminator. Interface emission opt-in with EmitInterfaces = true."
---

## Polymorphic types → discriminated unions

When a C# class hierarchy carries `[JsonPolymorphic]` + `[JsonDerivedType]`
(System.Text.Json's native polymorphism support), TypeGen emits a real
discriminated union — no manual union declaration, no virtual `Type` property
hack:

```csharp
[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi)]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Circle), "circle")]
[JsonDerivedType(typeof(Square), "square")]
public abstract record Shape;

public record Circle(double Radius) : Shape;
public record Square(double Side)   : Shape;
```

→ TypeScript:
```typescript
// Shape.ts
export type Shape = Circle | Square;

// Circle.ts
export interface Circle {
    kind: "circle";       // discriminator pinned as literal
    radius: number;
}

// Square.ts
export interface Square {
    kind: "square";
    side: number;
}
```

Frontend gets **full type narrowing for free**:
```typescript
function area(s: Shape): number {
    if (s.kind === "circle") return Math.PI * s.radius ** 2;   // TS knows: Circle
    return s.side ** 2;                                         // TS knows: Square
}
```

→ OpenAPI:
```yaml
Shape:
  oneOf:
    - $ref: '#/components/schemas/Circle'
    - $ref: '#/components/schemas/Square'
  discriminator:
    propertyName: kind
    mapping:
      circle: '#/components/schemas/Circle'
      square: '#/components/schemas/Square'

Circle:
  type: object
  required: [kind, Radius]
  properties:
    kind:
      type: string
      enum: [circle]        # pinned literal
    Radius: { type: number, format: double }
# (Square analogously)
```

**Detection is explicit — only types listed in `[JsonDerivedType]` attributes
become variants.** Auto-scanning all subclasses in the compilation would be
surprising (tests / sample code dangling off a production hierarchy would leak
into the API). If you add a new subclass, you add a `[JsonDerivedType]` line —
the attribute doubles as runtime JSON config AND TS/OpenAPI contract.

**Default discriminator name:** when `[JsonPolymorphic(TypeDiscriminatorPropertyName = "...")]`
is omitted, TypeGen uses `$type` (STJ's default). Match whatever your runtime
serializer expects.

**Variants auto-seeded:** the subclasses don't need their own `[GenerateTypes]`
— they're pulled in by the polymorphic seed pass with the base's targets and
output dir.

## Interfaces

By default TypeGen ignores C# interfaces — they're neither emitted nor wired
into implementing classes. Flip `TypeScript.EmitInterfaces = true` in the
configurator (single flag drives all three targets) and:

```csharp
b.TypeScript(ts => ts.EmitInterfaces = true);
```

- Every interface reached transitively from a `[GenerateTypes]` class gets
  its own schema (TS `interface`, OpenAPI `type: object`). Annotate with
  `[GenerateTypes]` directly to pull one in without an implementor.
- Implementing classes `extends I1, I2` in TS / `allOf: [{$ref: I1}, {$ref: I2}, {type: object, …}]`
  in OpenAPI, with inherited members deduplicated.
- `[TsIgnore]` / `[OpenApiIgnore]` on an interface **member** takes effect —
  the member never lands in the interface schema, and because the class
  doesn't redeclare inherited members, it's gone from the output entirely.
  This fixes a class of silent-swallow bugs where users put ignore attrs on
  interface properties expecting them to propagate.
- `[TsIgnore]` / `[OpenApiIgnore]` on the **interface itself** drops it from
  the target's `extends` / `allOf` chain. The class then keeps its own
  declaration of the inherited members so nothing is lost.
- Marker interfaces (no public properties) are silently skipped — emitting
  empty `type: object` schemas serves nobody.
- Generic interfaces (`IHasPayload<T>`) are seeded by their open form;
  implementing classes reference them with concrete type args
  (`extends IHasPayload<string>`).
