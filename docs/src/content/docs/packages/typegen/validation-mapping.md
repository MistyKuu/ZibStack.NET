---
title: TypeGen — Validation → OpenAPI constraints
description: "How DataAnnotations and ZibStack.Validation attributes translate to OpenAPI schema constraints. Includes required semantics vs NRT nullability."
---

DataAnnotations and ZibStack.Validation attributes are translated to the
matching schema constraints in the emitted OpenAPI document. No annotations
need changing on the DTO — if the attribute is already there for runtime
validation, TypeGen picks it up.

| Attribute | OpenAPI constraint |
|---|---|
| `[MinLength(n)]`, `[ZMinLength(n)]` | `minLength: n` |
| `[MaxLength(n)]`, `[ZMaxLength(n)]` | `maxLength: n` |
| `[StringLength(max, MinimumLength=min)]` | `minLength: min`, `maxLength: max` |
| `[Range(min, max)]`, `[ZRange(min, max)]` | `minimum: min`, `maximum: max` |
| `[RegularExpression(pat)]`, `[ZMatch(pat)]` | `pattern: "pat"` |
| `[EmailAddress]`, `[ZEmail]` | `format: email` |
| `[Url]`, `[ZUrl]` | `format: uri` |
| `[ZNotEmpty]` | `minLength: 1` (approximation for strings/arrays) |
| `[Required]`, `[ZRequired]`, C# 11 `required` modifier | field lands in `required:` list, non-optional across TS / Zod / Python |

Attribute detection is by metadata name — no runtime dependency on either
package. Explicit `[OpenApiProperty]` fields win (don't get overwritten).

**`required` vs NRT nullability.** By default TypeGen infers required from NRT —
`string` is required, `string?` isn't. Explicit `[Required]` / `[ZRequired]` /
C# `required` modifier **override NRT**: even a `string?` annotated with
`[Required]` lands in the required list, gets no optional `?` in TypeScript, no
`.nullish()` in Zod, no `| None` default in Pydantic. Matches the wire-level
contract your runtime validator enforces.
