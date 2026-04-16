---
title: TypeGen — Type mapping
description: "How C# types map to TypeScript, OpenAPI, and Python. Includes [TsType] with auto-imports and the cross-target generic [UseType<T>] override."
---

The emitters translate C# types to target-language equivalents. Defaults:

| C# | TypeScript | OpenAPI | Python (Pydantic) |
|---|---|---|---|
| `int`, `long`, `short` | `number` | `integer` (`int32`/`int64`) | `int` |
| `float`, `double` | `number` | `number` (`float`/`double`) | `float` |
| `decimal` | `string` (precision!) | `number` (`double`) | `str` (precision!) |
| `string` | `string` | `string` | `str` |
| `bool` | `boolean` | `boolean` | `bool` |
| `System.Guid` | `string` | `string` (`uuid`) | `UUID` |
| `System.DateTime` | `string` | `string` (`date-time`) | `datetime` |
| `System.DateOnly` | `string` | `string` (`date`) | `date` |
| `T?` (nullable) | optional `?` | `nullable: true` + not in `required` | `T \| None = None` |
| `List<T>`, `T[]` | `T[]` | `type: array, items: ...` | `list[T]` |
| `Dictionary<K, V>` | `Record<K, V>` | `type: object, additionalProperties: V` | `dict[K, V]` |
| User DTO | `TypeName` | `$ref: '#/components/schemas/TypeName'` | `TypeName` (imported) |
| `enum` | `export enum` (numeric values) | `type: string, enum: [...]` | `IntEnum` |
| `enum` with `[JsonConverter(typeof(JsonStringEnumConverter))]` | `export enum` (string values) | `type: string, enum: [...]` | `(str, Enum)` |

Override any single property with `[TsType("...")]` or `[OpenApiProperty(Format = "...")]`.

## `[TsType]` with imports

When the type expression names an external symbol (your own DTO, a third-party
type, a branded type alias) the generator can emit the matching `import` line
for you. Pass `ImportFrom` as a named argument:

```csharp
[TsType("AutomationRulePayload", ImportFrom = "./types/automation-rule-payload")]
public JsonObject? Element { get; set; }
```

→ at the top of the generated file:
```typescript
import { AutomationRulePayload } from './types/automation-rule-payload';
```

Multiple PascalCase identifiers in the expression all get pulled from the same
path:

```csharp
[TsType("Map<Foo, Bar>", ImportFrom = "./types/api")]
public object Item { get; set; }
```

→
```typescript
import { Bar, Foo, Map } from './types/api';
```

Primitives (`string`, `number`, `boolean`), literal unions (`'a' | 'b'`) and
similar non-importable tokens are left alone. Two properties pointing at the
same path get merged into one `import` line; different paths get separate lines.

The same is available via the fluent configurator — `.TsType("Foo", "./bar")`:

```csharp
b.ForType<Article>()
    .Property(a => a.Element).TsType("AutomationRulePayload", "./types/automation-rule-payload");
```

When `ImportFrom` is null / omitted (or the type expression is a primitive like
`"string"`), no import is emitted — the override is treated as opaque.

## `[UseType<T>]` — cross-target generic override (C# 11+)

Refactor-safe, cross-target replacement for the string form. One attribute —
every emitter handles it in its own idiom:

| target | emitted |
|---|---|
| TypeScript | `prop: T;` + `import { T } from './T';` (path auto-computed) |
| OpenAPI | `$ref: '#/components/schemas/T'` |
| Python | `prop: T` + `from t import T` |

```csharp
[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi, OutputDir = ".")]
public class Rule
{
    [UseType<AutomationRulePayload>]
    public JsonObject? Element { get; set; }
}

[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi, OutputDir = ".")]
public class AutomationRulePayload { public string Body { get; set; } = ""; }
```

→ TypeScript:
```typescript
import { AutomationRulePayload } from './AutomationRulePayload';

export interface Rule {
    element?: AutomationRulePayload;
}
```

→ OpenAPI:
```yaml
Rule:
  type: object
  properties:
    Element:
      $ref: '#/components/schemas/AutomationRulePayload'
```

**Cross-directory TS imports** — different `OutputDir` values, one per type:
```csharp
[GenerateTypes(OutputDir = "client/src/rules")] public class Rule {
    [UseType<Payload>] public object? El { get; set; }
}
[GenerateTypes(OutputDir = "client/src/types")] public class Payload { /* … */ }
```
→ `import { Payload } from '../types/Payload';` — the `..` up-traversal is
computed automatically from the two `OutputDir`s' common ancestor.

**External targets** (BCL types, NuGet packages, hand-written `.d.ts`) — the
symbol lives outside the current compilation so TS auto-path doesn't apply.
Pair with explicit `ImportFrom` (TS-only — OpenAPI / Python reference `T` by
name regardless):
```csharp
[UseType<ExternalLib.Widget>(ImportFrom = "@acme/widgets")]
public object? W { get; set; }
```

**[TsName] on the target is honored** — if `Payload` carries `[TsName("PayloadDto")]`,
both the TS type expression and the import use `PayloadDto`.

**Enums work too:** `[UseType<Priority>]` against `[GenerateTypes] public enum Priority { … }`.

**Fluent equivalent:**
```csharp
b.ForType<Rule>()
    .Property(r => r.Element).UseType<AutomationRulePayload>();
```

**When to use `[TsType("…")]` instead** — for TS-only opaque expressions that
have no semantic in OpenAPI or Python: literal unions (`"'pending' | 'done'"`),
branded types (`"string & { __brand: 'email' }"`), complex generics hand-written
in TypeScript. Those stay on the string form.

**Requires C# 11+** in the consuming project (generic attributes).
