# Zod 4.6 integration design

Branch: `zod-4-6-support`
Baseline: ZibStack.NET v3.2.7 / Zod 4.4.3
Target: Zod 4.6.1
Status: core integration implemented on `zod-4-6-support`; Zod Mini and
build-time `withParser` remain deferred pending bundle/performance evidence.

## Context

Zod 4.6.1 was published on September 9, 2026. The `4.6.1` release itself is a
small patch for discriminated-union defaults and recursive inference, plus a
locale addition. The larger opportunity is the combined feature set released
in Zod 4.5 and 4.6 since ZibStack's current `zod@4.4.3` integration-test pin.

Primary references:

- [Zod 4.5 release](https://github.com/colinhacks/zod/releases/tag/v4.5.0)
- [Zod 4.6 release](https://github.com/colinhacks/zod/releases/tag/v4.6.0)
- [Zod 4.6.1 release](https://github.com/colinhacks/zod/releases/tag/v4.6.1)
- [Changes from 4.4.3 to 4.6.1](https://github.com/colinhacks/zod/compare/v4.4.3...v4.6.1)

The current ZibStack emitter already uses Zod 4 factories such as `z.email()`,
`z.uuid()`, and `z.iso.datetime()`. It emits runtime schemas and inferred types,
but does not compile schemas, emit boolean validation helpers, connect schemas
to the TanStack client, model exact optional properties, or correctly break
recursive schema initialization cycles.

## Goals

1. Keep generated schemas correct and type-safe before adding performance modes.
2. Make validation of API payloads cheap enough to enable at client boundaries.
3. Preserve C# DTO wire semantics, especially patch fields and nullable values.
4. Keep the default output CSP-safe and backwards compatible.
5. Expose Zod-specific behavior without leaking it into the TypeScript and
   OpenAPI emitters.

## Non-goals

- Reimplement all of Zod in C#.
- Generate schemas for JavaScript class instances or symbol-keyed objects.
- Consume JSON Schema through `z.fromJSONSchema()`; ZibStack already owns a
  direct schema model and emitter.
- Automatically choose application locale.
- Generate arbitrary transforms or refinements from opaque C# methods.

## Proposed roadmap

### Phase 0 — compatibility baseline

Update `ZodCompilationTests` from `zod@4.4.3` to `zod@4.6.1` and keep the real
`tsc` compilation test in CI. Add fixtures for:

- discriminated unions;
- nullable and optional properties;
- dictionaries and arrays;
- recursive/self-referencing contracts;
- generated TypeScript and Zod output enabled together.

No generated syntax needs to change merely to consume 4.6.1. Users receive
Zod's lower schema memory usage, faster failure paths, safer formats, and bug
fixes by upgrading their npm dependency.

### Phase 1 — type conformance and correctness

#### 1. Assert schemas against emitted TypeScript

Zod 4.5 adds `z.toZod<T>()`. When TypeScript and Zod targets are both enabled,
offer an opt-in mode that makes `tsc` prove the runtime schema agrees with the
generated interface.

Proposed configuration:

```csharp
b.Zod(z =>
{
    z.ConformToTypeScriptTypes = true;
});
```

Proposed output:

```ts
import { z } from "zod";
import type { Order } from "./Order";

export const OrderSchema = z.toZod<Order>()(
  z.object({ id: z.number().int(), note: z.string().nullish() })
);
```

Keep today's `z.infer` behavior as the default for Zod-only generation. Report a
TypeGen diagnostic if conformance mode is requested without TypeScript output.

#### 2. Correct `PatchField<T>` optionality

Zod 4.5 adds exact optionals and `.exactPartial()`. A generated update DTO needs
to distinguish these wire states:

- property absent: do not modify the field;
- property present with `null`: explicitly clear a nullable field;
- property present with a value: update the field.

Emit a `PatchField<T>` property as exact-optional, with nullability applied to
the value schema:

```ts
displayName: z.exactOptional(z.string().nullable())
```

Do not globally replace `.optional()` with exact optionality. Read-only response
properties and ordinary nullable request properties have different semantics.
Carry an explicit `PropertyPresence` value in `SchemaProperty` instead of
inferring all presence behavior from `IsNullable` and `IsReadOnly`.

Suggested model:

```csharp
internal enum PropertyPresence
{
    Required,
    Optional,
    ExactOptional,
}
```

Use `z.deepPartial()` only for contracts explicitly declared as deep partials.
It should not be applied automatically to every update DTO because DTO ignore,
write-once, and nested-DTO rules can make its shape differ from the entity.

#### 3. Fix recursive schemas

The current emitter's comment says cycles are broken with `z.lazy()`, but the
emitter does not currently emit `z.lazy()`. Topological sorting alone cannot
order a cycle and can leave a JavaScript temporal-dead-zone reference.

Build a schema dependency graph, find strongly connected components, and wrap
only cyclic edges:

```ts
export const CategorySchema: z.ZodType<Category> = z.object({
  name: z.string(),
  children: z.array(z.lazy(() => CategorySchema)),
});
```

Cover self-cycles, two-type cycles, collections, nullable edges, file-per-class
ES-module cycles, and cyclic input data. Zod 4.5 can preserve cycles in input;
ZibStack first has to emit an initialization-safe schema graph.

### Phase 2 — performance features

#### 4. Optional schema compilation

Zod 4.5 adds `z.compile()`, which generates a fast parser using `new Function`.
Expose compilation as an explicit setting because strict Content Security
Policies can prohibit dynamic code generation.

```csharp
public enum ZodCompilationMode
{
    None,
    Compile,
}

b.Zod(z => z.Compilation = ZodCompilationMode.Compile);
```

Proposed output:

```ts
const OrderSchemaDefinition = z.object({ /* ... */ });
export const OrderSchema = z.compile(OrderSchemaDefinition);
```

Default to `None`. Do not emit the global `import "zod/compile"` side effect:
explicit compilation is deterministic, works with file-per-class output, and
does not depend on import order.

#### 5. Boolean validation guards

Zod 4.6 adds `.validate()`/`.validateAsync()`, which short-circuit without
building a parse result or issue array. Offer generated guards:

```csharp
b.Zod(z => z.EmitValidationGuards = true);
```

```ts
export const isOrder = (input: unknown): input is z.input<typeof OrderSchema> =>
  OrderSchema.validate(input);
```

These become especially useful with compiled schemas. Keep `.parse()` available
for boundaries that need normalized output or detailed validation errors.

### Phase 3 — TanStack Query integration

Today the generated TanStack client trusts `response.json()` and only uses C#
types at compile time. Add optional runtime response parsing with generated Zod
schemas:

```csharp
public enum QueryPayloadValidation
{
    None,
    Responses,
    RequestsAndResponses,
}

b.TanStackQuery(q =>
{
    q.PayloadValidation = QueryPayloadValidation.Responses;
});
```

Proposed response path:

```ts
const payload: unknown = await apiFetch(path, options);
return OrderSchema.parse(payload);
```

Design requirements:

- `None` remains the default and adds no Zod dependency to TanStack-only users.
- Enabling validation automatically requires or diagnoses missing Zod targets.
- Response types derive from `z.output<typeof Schema>`.
- Request types derive from `z.input<typeof Schema>`.
- Errors retain endpoint/operation context before surfacing through TanStack.
- Compiled schemas are reused; never compile per request.

This is the highest-value cross-package feature: server DTO metadata becomes a
runtime contract at the actual network boundary, not merely a TypeScript hint.

### Phase 4 — richer checks and formats

The new exported schema factories since Zod 4.4.3 are `z.creditCard()`,
`z.iban()`, and `z.properties()`.

ZibStack.NET.Validation already has `[ZCreditCard]`, its fluent `.CreditCard()`
equivalent, and generated Luhn validation. TypeGen does not read that attribute
today. Make this the first format bridge:

```csharp
public sealed class PaymentRequest
{
    [ZCreditCard]
    [Sensitive]
    public string CardNumber { get; init; } = "";

    [ZIban]
    public string Iban { get; init; } = "";
}
```

Mappings:

```ts
cardNumber: z.creditCard()
iban: z.iban()
```

For credit cards, teach TypeGen to recognize both
`ZibStack.NET.Validation.ZCreditCardAttribute` and
`System.ComponentModel.DataAnnotations.CreditCardAttribute`, then emit
`z.creditCard()` and suitable OpenAPI metadata. Reconcile one existing semantic
difference first: Zod accepts 12–19 digits while ZibStack's generated validator
previously accepted 13–19. The generated C# rule is now aligned to Zod's 12–19
range so the same value does not pass on one side of the API and fail on the other.

For IBAN, add `[ZIban]` plus a fluent `.Iban()` rule to
`ZibStack.NET.Validation`, implement electronic-format ISO 7064 MOD 97-10
validation, and teach TypeGen to emit `z.iban()`.

Add parity fixtures that feed the same valid and invalid corpus to generated C#
validation and Zod. This prevents a request from passing on one side of the API
boundary and failing on the other.

`z.properties()` validates properties in place while preserving a JavaScript
instance's prototype. JSON API DTOs are plain objects, so it does not warrant a
general ZibStack attribute. Keep it as an escape-hatch/consumer concern unless
TypeGen later supports browser/runtime types such as `Response`, `URL`, or
third-party class instances.

Related check improvements worth exposing where the C# model carries equivalent
metadata:

- custom NanoID length (`z.nanoid({ length })`);
- exact optional/partial semantics for patch DTOs;
- JSON Schema collection/object constraints (`uniqueItems`, `contains`,
  `minContains`, `maxContains`, `minProperties`, `maxProperties`).

The last group is newly enforced by `z.fromJSONSchema()` rather than being new
direct schema factories. ZibStack emits Zod directly, so support should come from
neutral schema-model constraints, not by routing generated output through JSON
Schema.

Also add a Zod-specific escape hatch for formats that do not have a server-side
rule yet:

```csharp
b.ForType<Account>()
    .Property(x => x.ExternalId)
    .ZodFormat(ZodStringFormat.Ulid);
```

Do not keep routing Zod behavior through `OpenApiFormat`; introduce a neutral
wire-format field for shared semantics and a Zod-only override for intentional
target-specific behavior.

Candidate format enum values:

- `Email`, `Url`, `Uuid`, `Date`, `DateTime`;
- `Hostname`, `Ulid`, `NanoId`;
- `Base64`, `Base64Url`;
- `CreditCard`, `Iban`.

### Phase 5 — optional Zod Mini backend

Zod 4.5 publishes `@zod/mini` as a standalone package. Add a separate emitter
flavor only after measuring generated bundle size:

```csharp
public enum ZodFlavor
{
    Classic,
    Mini,
}
```

Mini is not an import-path substitution. Current fluent chains such as
`.min()`, `.max()`, and `.regex()` must be emitted as Mini checks, so isolate
schema construction behind a small internal expression model before supporting
both backends.

### Future — CSP-safe generated parsers

Zod 4.6's `z.withParser()` can install a parser generated elsewhere without
`new Function`. ZibStack is itself a build-time generator, so it could emit a
specialized TypeScript parser and attach it to the Zod schema:

```ts
export const OrderSchema = z.withParser(OrderSchemaDefinition, parseOrder);
```

This could offer compiled-schema performance under strict CSP, but it is a
large project. The generated parser must exactly preserve Zod output semantics:
unknown-key stripping, nested output rebuilding, defaults, catches, codecs,
unions, and error fallback. Start only with a restricted set of pure schemas,
return `z.INVALID` for unsupported or failing paths, and differential-test every
generated parser against normal Zod parsing.

## Features that need no ZibStack API

These Zod improvements are automatically inherited after the npm upgrade:

- lower per-schema memory use;
- faster CommonJS exports and failure paths;
- fixed email, IPv6, ULID, emoji, base64, and Unicode-length behavior;
- recursive-input memory retention fixes;
- corrected numeric enum options;
- corrected JSON Schema constraint folding;
- locale additions;
- `z.getDiscriminatedOption()` for consumer code.

`z.properties()`, JavaScript symbol keys, and `z.fromJSONSchema()` do not map
naturally to JSON DTO generation and should remain consumer-level Zod features.

## Compatibility strategy

Do not silently emit 4.5/4.6-only APIs for every existing project. Add a target
capability setting and diagnostics:

```csharp
b.Zod(z =>
{
    z.TargetVersion = ZodTargetVersion.V4_6;
});
```

- Existing configurations preserve current output.
- Enabling compilation, validation guards, exact optionals, IBAN, or
  `z.toZod<T>()` implies a 4.5/4.6 capability and emits a clear diagnostic when
  the configured target is too old.
- Documentation should show `npm install zod@^4.6.1` for the new profile.
- Generated files should include the target in the banner for troubleshooting:
  `// @generated by ZibStack.NET.TypeGen (Zod >= 4.6.1)`.

ZibStack cannot reliably inspect a frontend's installed npm version from a
Roslyn generator, so this is a declared compatibility target, not automatic
package discovery.

## Suggested delivery order

1. Compatibility pin and recursive-schema correctness.
2. `z.toZod<T>()` conformance mode and exact `PatchField<T>` semantics.
3. Compilation and validation guards.
4. TanStack response validation.
5. IBAN/credit-card validation parity.
6. Zod Mini after bundle measurements.
7. CSP-safe external parsers only after differential-test infrastructure exists.

The first three items are cohesive enough for one minor TypeGen release. Runtime
TanStack validation and new server-side validation attributes should be separate
features because they cross package boundaries and deserve independent opt-in
and release notes.
