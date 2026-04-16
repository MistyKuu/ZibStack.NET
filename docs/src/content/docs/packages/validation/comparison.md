---
title: ZibStack.NET.Validation — Alternatives
description: "How ZibStack.NET.Validation compares to DataAnnotations, FluentValidation (deprecated ASP.NET integration in v12), and raw source generators — compile-time rules, zero reflection."
---

Validation in .NET splits into two worlds: attributes on properties (DataAnnotations — built in, limited), and fluent code (FluentValidation — flexible, runtime). ZibStack.NET.Validation **combines the ergonomics of attributes** (stay on the DTO where the fields are) **with the performance of a source generator** (compile-time, zero reflection, `TryValidate` / `Validate` methods generated directly on the type).

| Feature | **ZibStack.NET.Validation** | DataAnnotations | FluentValidation |
|---|---|---|---|
| Rule location | Attributes on properties (`[ZRequired]`, `[ZRange]`, `[ZEmail]`, `[ZNotEmpty]`, …) | Attributes on properties | Separate `AbstractValidator<T>` class |
| Rule evaluation | **Generated method body at compile time** | Runtime reflection (`Validator.TryValidateObject`) | Compiled expression trees (runtime) |
| Reflection on hot path | ❌ zero | ✅ yes | partial |
| AOT-friendly | ✅ no reflection, no expressions | partial (some attributes reflect) | ❌ expression trees |
| Custom rules | `[ZMatch(pattern)]` + `ValidationAttribute` subclass still works | `ValidationAttribute` subclass | `Must(x => …)` / custom validator |
| Conditional rules | partial (via `[Required]` + NRT) — plain conditional via custom attribute | ❌ | ✅ `When(x => …)` |
| Async rules | ❌ (source gen can't emit await — out of scope) | ❌ | ✅ `MustAsync` |
| Multiple errors returned | ✅ `ValidationResult.Errors` | ✅ `ValidationResult[]` | ✅ `ValidationResult.Errors` |
| ASP.NET Core auto-integration | ✅ runs via ProblemDetails or [Dto] endpoints | ✅ built-in | ⚠️ `FluentValidation.AspNetCore` **deprecated in v12** |
| DTO / TS / OpenAPI cross-target consistency | ✅ picked up by ZibStack.NET.TypeGen → OpenAPI `minLength`, `pattern`, Zod `.email()`, `.gte()` | n/a | n/a |
| Install footprint | NuGet (source gen only, no runtime lib) | zero (BCL) | NuGet |

## What you give up

Async validation and richly conditional rules (`.When(...).Otherwise(...)`) — FluentValidation still wins when your rule needs to hit a database (`MustAsync(async x => await repo.ExistsAsync(x))`). Source-generated validation intentionally stays synchronous + expression-free.

## When to pick ZibStack.NET.Validation

You want the ergonomic simplicity of DataAnnotations (rules live on the property, one-liner) **plus** zero-reflection performance **plus** the rules flow automatically to your OpenAPI / Zod / TypeScript contracts via ZibStack.NET.TypeGen. No separate validator class to keep in sync with the DTO, no runtime cost that shows up on hot paths or in AOT builds.

## When to stay on alternatives

- **FluentValidation** — you need async rules (DB lookups, external service calls), complex cross-field conditionals, or your team already has a large validator library invested.
- **DataAnnotations vanilla** — tiny app, one-off validation, don't care about perf, don't need cross-target contract generation.

## Sources

- [Data Annotations vs FluentValidation (roundthecode.com)](https://www.roundthecode.com/dotnet-blog/data-annotations-vs-fluentvalidation-which-should-you-use)
- [FluentValidation 12 release notes (ASP.NET integration deprecated)](https://github.com/FluentValidation/FluentValidation)
- [System.ComponentModel.DataAnnotations reference](https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.dataannotations)
