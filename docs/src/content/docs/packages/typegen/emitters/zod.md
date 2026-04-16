---
title: TypeGen — Zod emitter (runtime validation)
description: "TypeTarget.Zod emits Zod schemas — TypeScript source that ships a runtime validator alongside an inferred type. Pairs with the TypeScript target or runs standalone."
---

`TypeTarget.Zod` emits [Zod](https://zod.dev/) schemas — TypeScript source that
ships a runtime validator alongside an inferred type. Pairs with the TypeScript
target (both can emit in parallel, independent files, zero coupling) or runs
standalone when you want only Zod without a separate interface file.

```csharp
[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.Zod,
               OutputDir = "../client/src/api")]
public class Order
{
    public int Id { get; set; }
    [ZEmail] public string Email { get; set; } = "";
    [ZRange(1, 100)] public int Qty { get; set; }
    public OrderStatus Status { get; set; }
}
```

→ **`Order.ts`** (TypeScript emitter, unchanged):
```typescript
export interface Order {
    id: number;
    email: string;
    qty: number;
    status: OrderStatus;
}
```

→ **`Order.schema.ts`** (Zod emitter, new):
```typescript
import { z } from 'zod';
import { OrderStatusSchema } from './OrderStatus.schema';

export const OrderSchema = z.object({
    id: z.number().int(),
    email: z.string().email(),
    qty: z.number().int().gte(1).lte(100),
    status: OrderStatusSchema,
});
export type Order = z.infer<typeof OrderSchema>;
```

**Independent from TypeScript emitter.** Both files are generated from the same
`SchemaModel`, so drift is structurally impossible — change the C# class and
both regen identically on the next build. The TS interface stays as the
ergonomic type-only view (cheap to import, no runtime dep); the Zod schema
carries the runtime validator and its own `z.infer` alias for Zod-only consumers.

## Validation constraint mapping

Same attributes that drive OpenAPI constraints map to Zod chained calls:

| C# | Zod |
|---|---|
| `[MinLength(n)]`, `[ZMinLength(n)]` | `.min(n)` |
| `[MaxLength(n)]`, `[ZMaxLength(n)]` | `.max(n)` |
| `[Range(min, max)]`, `[ZRange(min, max)]` | `.gte(min).lte(max)` |
| `[RegularExpression("pat")]`, `[ZMatch("pat")]` | `.regex(/pat/)` |
| `[EmailAddress]`, `[ZEmail]` | `.email()` |
| `[Url]`, `[ZUrl]` | `.url()` |
| `System.Guid` | `z.string().uuid()` |
| `System.DateTime` | `z.string().datetime()` |
| `System.DateOnly` | `z.string().date()` *(Zod 3.23+)* |

## Type mapping

| C# | Zod |
|---|---|
| `int`, `long`, `short`, `byte` | `z.number().int()` |
| `float`, `double` | `z.number()` |
| `decimal` | `z.string()` *(precision-preserving, matches TS)* |
| `string` | `z.string()` |
| `bool` | `z.boolean()` |
| `T?` (nullable) | `.nullish()` *(null ∪ undefined ∪ absent)* |
| `List<T>`, `T[]` | `z.array(T)` |
| `Dictionary<string, V>` | `z.record(z.string(), V)` |
| user DTO | direct ref `{Name}Schema` (cross-file import) |
| numeric `enum` | `z.union([z.literal(0), z.literal(1), …])` |
| `enum` + `[JsonStringEnumConverter]` | `z.enum(['A', 'B', …])` |

## Polymorphic unions

`[JsonPolymorphic]` + `[JsonDerivedType]` on the C# side produces a
`z.discriminatedUnion` — matching the TypeScript discriminated-union semantics
exactly, with exhaustive narrowing from the discriminator literal:

```typescript
export const ShapeSchema = z.discriminatedUnion('kind', [
    CircleSchema,
    SquareSchema,
]);
export type Shape = z.infer<typeof ShapeSchema>;

// In CircleSchema:
export const CircleSchema = z.object({
    kind: z.literal('circle'),
    radius: z.number(),
});
```

## Inheritance

When the base class is in the emit set, derived schemas compose via `.extend()`:

```typescript
// Derived class Order : Entity
export const OrderSchema = EntitySchema.extend({
    customer: z.string(),
});
```

## Configuration

```csharp
b.Zod(z =>
{
    z.OutputDir = "../client/src/validation";
    z.FileLayout = ZodFileLayout.SingleFile;
    z.SingleFileName = "schemas.ts";
    z.SchemaConstSuffix = "Schema";   // default; "XxxSchema"
    z.EmitInferredTypes = true;       // default; adds `export type X = z.infer<…>`
    z.FileSuffix = ".schema";         // `Order.schema.ts` avoids collision with TS's `Order.ts`
});
```

**Consumer install:** the emitted code imports `zod` — add it to the frontend
project: `npm install zod`. TypeGen doesn't bundle or generate the dep.
