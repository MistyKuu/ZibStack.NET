# ZibStack.NET.TypeGen

Roslyn source generator that emits **TypeScript** (`.ts`), **OpenAPI 3.0**
(`.yaml` / `.json`), and **TanStack Query** clients from C# DTOs/endpoints
annotated with `[GenerateTypes]`. Optional **Python** (Pydantic v2 / dataclass)
output. Compile-time only, zero reflection, no running app required.

## What it does

```csharp
[GenerateTypes(Targets = TypeTarget.TypeScript | TypeTarget.OpenApi, OutputDir = "generated")]
public class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public List<OrderItem> Items { get; set; } = new();
    public OrderStatus Status { get; set; }
}

public class OrderItem { public int Qty { get; set; } public Product Product { get; set; } = new(); }
public class Product { public string Sku { get; set; } = ""; }
public enum OrderStatus { Pending, Shipped }
```

→ at `dotnet build` (or on save in IDE — see below):

- `generated/Order.ts`, `OrderItem.ts`, `Product.ts`, `OrderStatus.ts` — each with cross-file
  `import { X } from './X';` statements that compile directly with `tsc`.
- `generated/openapi.yaml` — OpenAPI 3.0.3 schema with every class under
  `components/schemas`, correct `$ref`s, and `nullable` / `required` / validation
  constraints pulled from `ZibStack.NET.Validation` attributes.
- `generated/api.gen.ts` — optional TanStack Query v5 helpers from discovered
  Minimal API, controller, and `[CrudApi]` endpoints: typed fetch functions,
  query keys, options factories, hooks, and invalidation/prefetch helpers.

Nested types without their own `[GenerateTypes]` are auto-discovered by walking
the property graph, so you only annotate root aggregates.

## Highlights

- **Transitive type discovery** — nested objects, enums, collections, dictionaries
  all pulled in automatically.
- **C# inheritance preserved** — emitted TS keeps the full `extends` chain;
  OpenAPI uses `allOf`.
- **`[TsType<T>]` / `[TsType("expr", ImportFrom = "./…")]`** — override the TS
  type expression. Generic form auto-computes the relative import path.
- **`[JsonStringEnumConverter]` aware** — string-valued TS enums + Python
  `(str, Enum)` when the enum carries the converter attribute.
- **`[JsonExtensionData]` → schema-level `additionalProperties`** (OpenAPI) /
  index signature `[key: string]: unknown` (TypeScript).
- **`[CrudApi]` integration** with `ZibStack.NET.Dto` — emits OpenAPI `paths`
  (GET list, GET by id, POST, PATCH, DELETE) with the right request/response
  schema refs, pagination wrappers, and query-string bindings for
  `ZibStack.NET.Query` when referenced.
- **TanStack Query emitter** — generates React Query v5 clients from the same
  endpoint discovery model as OpenAPI. Supports Minimal API `.WithName(...)` /
  `.WithTags(...)`, custom API clients, split-by-tag output, and cache helpers.
- **Fluent project-wide config** via `ITypeGenConfigurator` — global output dir,
  file layout (`FilePerClass` / `SingleFile`), naming styles, per-type overrides,
  property-level overrides.
- **Live regen on save** — generator writes `.ts` / `.yaml` files directly from
  the Roslyn pipeline, so your frontend dev server picks changes up without a
  full `dotnet build`. Falls back to an MSBuild post-compile target in
  sandboxed analyzer hosts.
- **Stale file sweep** — renames drop old files; the sweep is banner-gated so
  hand-written files in the same directory are safe.

## Install

```bash
dotnet add package ZibStack.NET.TypeGen
```

That's it — `ZibStack.NET.TypeGen.Abstractions` (attributes + settings types)
is pulled in transitively. The analyzer self-registers; everything else is
in the attribute / configurator surface.

## Docs

Full reference — type mapping, diagnostic list (`TG0001`-`TG0021`), fluent DSL,
`[CrudApi]` integration, TanStack Query, Python/Zod emitters, file layout options — lives at
[mistykuu.github.io/ZibStack.NET/packages/typegen](https://mistykuu.github.io/ZibStack.NET/packages/typegen/).

## License

MIT. See the repository root `LICENSE` file.
