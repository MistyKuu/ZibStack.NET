---
title: TypeGen — Advanced type features
description: "[JsonExtensionData] → additionalProperties, computed/immutable property handling, string-enum converters, transitive discovery of nested types, dictionaries and inheritance flattening rules."
---

## `[JsonExtensionData]` → schema-level `additionalProperties`

When a property carries `[JsonExtensionData]` (System.Text.Json or Newtonsoft.Json),
that property is **not** emitted as a regular field. Instead it bumps the parent
schema with `additionalProperties` (OpenAPI) / an index signature (TypeScript) —
matching what the runtime serializer actually does (catch every unmapped JSON key).

```csharp
[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi)]
public class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, object?> Extra { get; set; } = new();
}
```

→ TypeScript:
```typescript
export interface Order {
    id: number;
    customer: string;
    [key: string]: unknown;   // catches unmapped JSON keys
}
```

→ OpenAPI:
```yaml
Order:
  type: object
  required: [Id, Customer]
  properties:
    Id: { type: integer, format: int32 }
    Customer: { type: string }
  additionalProperties: true
```

**Typed value variant.** When the dictionary's value type is concrete (e.g.
`Dictionary<string, int>`, `Dictionary<string, Tag>`), the emitters carry the
type through:

- TypeScript: `[key: string]: number | unknown;` — union with `unknown` keeps
  named properties (which may not satisfy `number`) compatible with the index
  signature in strict mode.
- OpenAPI: `additionalProperties: { type: integer, format: int32 }` (or
  `{ $ref: ... }` for user-DTO values).

**Inheritance.** When the parent class is in the model, a derived class with
`[JsonExtensionData]` emits its index signature inside the body of the
`extends`/`allOf` shape — base properties + derived-only props + the additional
properties marker, all in the right place.

## Computed & immutable properties

The C# accessor shape drives Create/Update participation and the generated
client contract:

| C# property                   | TS                   | OpenAPI                      | Python (Pydantic)                 | Dto `Create` | Dto `Update` |
| ----------------------------- | -------------------- | ---------------------------- | --------------------------------- | ------------ | ------------ |
| `public int X { get; set; }`  | `x: number;`         | `required`                   | `x: int`                          | ✓            | ✓            |
| `public int X { get; init; }` | `x: number;`         | `required`                   | `x: int`                          | ✓            | — *(init)*   |
| `public int X { get; }`       | `readonly x?: number;` | `readOnly: true` + **not** required | `x: int \| None = Field(default=None, frozen=True)` | —            | —            |
| `public int X => Y * Z;`      | `readonly x?: number;` | `readOnly: true` + **not** required | `x: int \| None = Field(default=None, frozen=True)` | —            | —            |
| `public int X { get; private set; }` | `readonly x?: number;` | `readOnly: true` + **not** required | `x: int \| None = Field(default=None, frozen=True)` | —            | —            |

Why the `?` / optional: TypeGen emits a single schema per type, used for both
reading responses and constructing request payloads. Leaving computed fields
strictly required would force clients to provide values they shouldn't set
(server-owned), so readonly props become **optional at the TS / Python level
and excluded from the OpenAPI `required` list**. On the response side this
still types-checks — consumers read the value normally; it's just not
enforced at the type system during construction. `readOnly: true` / `readonly` /
`frozen=True` still prevent mutation after the value lands on the object.

`init`-only properties participate in `Create` (that's what `init` is for —
ctor-time assignment), but drop out of `Update` (an `init` accessor rejects
post-construction writes at runtime). Records with positional syntax
(`public record Order(string Sku)`) fall under this bucket — `Sku` ends up in
Create, not Update, matching the record's immutable-by-design nature.

## String enum converters

When an enum carries `[JsonConverter(typeof(JsonStringEnumConverter))]` (or the
generic `JsonStringEnumConverter<T>` in .NET 8+, or Newtonsoft's
`StringEnumConverter`), runtime JSON uses the **member name**, not the
underlying integer. TypeGen picks this up and emits matching client code so
deserialisation lines up automatically:

```csharp
[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.Python)]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrderStatus { Pending, Shipped, Delivered }
```

→ TypeScript:
```typescript
export enum OrderStatus {
    Pending = "Pending",
    Shipped = "Shipped",
    Delivered = "Delivered",
}
```

→ Python (`(str, Enum)` idiom — portable across 3.8+; `StrEnum` arrived in 3.11):
```python
from enum import Enum

class OrderStatus(str, Enum):
    PENDING = "Pending"
    SHIPPED = "Shipped"
    DELIVERED = "Delivered"
```

Without the converter the defaults stay — numeric TS enum, `IntEnum` in Python.
OpenAPI always emits `type: string, enum: [...]` because that's what the OpenAPI
ecosystem expects; numeric-enum integer discriminators are rarer and use
`$ref` or explicit `type: integer` overrides instead.

Non-standard converters (custom `JsonConverter<T>` subclasses) don't flip the
flag — TypeGen doesn't guess their serialised shape, so members still emit as
integers. Use `[TsType]` / `[OpenApiProperty]` to override per property.

## Transitive discovery of nested types

`[GenerateTypes]` only needs to go on **root** types — the generator walks every
public property recursively and pulls in any user-defined class, record, struct
or enum it finds, inheriting `Targets` and `OutputDir` from whichever root
reached it. Without this the reference types would fall through to `unknown`
in TS / `type: object` in OpenAPI, producing output that doesn't type-check.

```csharp
[GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = "../client/src/api")]
public class Order
{
    public Customer Buyer { get; set; } = new();   // ← Customer has NO [GenerateTypes]
    public List<LineItem> Lines { get; set; } = new();
    public OrderStatus Status { get; set; }
}

public class Customer { public string Name { get; set; } = ""; }
public class LineItem { public int Qty { get; set; } public Product Item { get; set; } = new(); }
public class Product { public string Sku { get; set; } = ""; }
public enum OrderStatus { Pending, Shipped }
```

All of `Customer`, `LineItem`, `Product`, `OrderStatus` get emitted into
`../client/src/api/` alongside `Order.ts`, with the right cross-file imports
(`import { Customer } from './Customer';` etc.).

**What counts as "user-defined":**
- Declared in the current compilation's own assembly (not NuGet packages)
- Namespace isn't `System.*`, `Microsoft.*`, or `Newtonsoft.*`
- Class, record, struct, or enum — primitives (`int`, `string`, `Guid`,
  `DateTime`, `decimal`, etc.) stay mapped to TS primitives / OpenAPI scalars

**Collection unwrapping:** `List<T>`, `T[]`, `IEnumerable<T>`, `HashSet<T>`,
`Dictionary<K, V>` and the common readonly / interface variants are walked
transparently — the generator discovers `T` (and `V` for dictionaries) without
you writing anything extra.

**Cycles** (`Node.Parent : Node?`, `A→B→A`) terminate via a visited-set keyed
by symbol identity — each type is emitted exactly once.

**Overrides win over discovery.** Attributes and fluent config on a discovered
type still apply:

```csharp
// Attribute on a nested class — still honored.
[TsName("BuyerDto")]
public class Customer { ... }

// Fluent on a discovered type — also honored. The fluent pass runs again
// after discovery, so freshly-pulled-in types go through the same merge.
b.ForType<Customer>()
    .TsName("BuyerDto")
    .Property(c => c.Name).TsName("displayName");
```

If a discovered type happens to carry its own `[GenerateTypes]` attribute with
different `Targets` / `OutputDir`, that explicit configuration wins — discovery
never overwrites it.

**Opt out** with `[TsIgnore]` / `[OpenApiIgnore]` on the nested class, or
`[TsIgnore]` on the property itself (skips the reference entirely so the type
isn't walked through that path).

## Dictionaries, inheritance

- `Dictionary<string, V>` (and `IDictionary` / `IReadOnlyDictionary`) emits as
  `{ type: object, additionalProperties: <V-schema> }`. Non-string keys are tolerated
  but key typing isn't preserved — OpenAPI only supports string keys.

### Inheritance — structure preserved, not flattened

The emitted TS / OpenAPI mirrors the C# inheritance chain **1:1**. Every base
class becomes its own schema, each level owns only its declared members, and
`extends` / `allOf` wires the hierarchy together. Un-annotated bases get
auto-seeded into the model with the descendant's `Targets` + `OutputDir`:

```csharp
public class Entity { public int Id { get; set; } }
public class Timestamped : Entity { public DateTime CreatedAt { get; set; } }
public class Auditable : Timestamped { public string CreatedBy { get; set; } = ""; }

[GenerateTypes(Targets = TypeTarget.TypeScript, OutputDir = "generated")]
public class Order : Auditable { public string Customer { get; set; } = ""; }
```

→ four TS files, each owning only its declared members:

```ts
// Entity.ts            → interface Entity { id: number; }
// Timestamped.ts       → interface Timestamped extends Entity { createdAt: string; }
// Auditable.ts         → interface Auditable extends Timestamped { createdBy: string; }
// Order.ts             → interface Order extends Auditable { customer: string; }
```

Mixed `[GenerateTypes]` on some levels works the same way — any class already
in the model stays as-is, un-annotated intermediates are auto-seeded. No
duplication anywhere: a member declared on `Entity` appears only in `Entity.ts`,
not copied into every descendant.

**Flattening still happens** when the base can't stand on its own — generic
bases (`Foo<T>`, out of MVP scope per `TG0003`) and BCL types have their
declared members inlined into the nearest emittable descendant so properties
aren't lost. Everything else preserves the chain.

**Abstract overrides.** When an abstract member on a non-emittable ancestor is
overridden by an intermediate in the chain, the override wins — the member
lands on the class that declared the concrete body, once, via standard
name-keyed dedupe.
