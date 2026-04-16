---
title: ZibStack.NET.Result — Alternatives
description: "How ZibStack.NET.Result compares to ErrorOr, FluentResults, OneOf, and LanguageExt — scope, learning curve, modeling philosophy."
---

The "Result pattern" for functional error handling in .NET has four well-known libraries, each with a different philosophy:

- **FluentResults** — one result type, error collection, metadata-heavy
- **OneOf** — true generic discriminated unions (`OneOf<T1, T2, T3>`), maximum type precision
- **ErrorOr** — middle ground, `ErrorOr<TValue>` with typed `Error` records
- **LanguageExt** — full functional-programming stack (Option, Try, Either, …) — powerful but a lot to learn

ZibStack.NET.Result sits in the same space, **opinionated toward pragmatic server-side APIs**: one `Result<T>` / `Result<T, TError>` with typed errors, Railway-oriented composition (`Then` / `Map` / `Bind`), and ergonomic converters to ASP.NET Core `ProblemDetails` / HTTP responses.

| Feature | **ZibStack.NET.Result** | FluentResults | OneOf | ErrorOr | LanguageExt |
|---|---|---|---|---|---|
| Philosophy | pragmatic Result + Error, HTTP-friendly | one result + collection of errors | true discriminated unions | typed `Error` records + result | full FP stack (Option/Try/Either) |
| Learning curve | low | low | low–medium | low | **high** (FP background needed) |
| Multiple outcome branches | ✅ `Result<T, TError>` typed error channel | single error bag | ✅ via `OneOf<T1, T2, T3>` | single error | ✅ via `Either` |
| ASP.NET Core `ProblemDetails` helpers | ✅ native `ToProblemDetails()` / `ToHttpResult()` | community | ❌ | ✅ | ❌ |
| Railway ops (Map / Bind / Then) | ✅ | ✅ | partial (via switch) | ✅ | ✅ deep |
| Pattern matching on outcome | ✅ `.Match(val, err)` | `.IsSuccess` / `.Errors` | ✅ `Match(a => …, b => …)` | `.Match(val, err)` | ✅ |
| Error metadata / hierarchy | typed `Error` with `Type`/`Code`/`Description` + metadata | ✅ rich `IError` objects | via `TError` | typed `Error` with category | via `Error` type |
| Implicit conversions (`return value;` / `return error;`) | ✅ | partial | partial | ✅ | ❌ |
| Runtime cost | struct-based where possible | alloc per result | union-sized struct possible | struct | variable |
| Install footprint | small | small | small | small | **large** |

## What you give up

Not a full functional-programming stack. If you want `Option<T>` instead of `T?`, `Try<T>` for exception wrapping, `IEnumerable` as `Seq<T>` with FP methods, persistent collections, etc. — that's LanguageExt's turf and this package doesn't compete there.

Not a true N-way discriminated union. `Result<T, TError>` is two branches; if you need three (e.g. `NotFound | Forbidden | Ok<T>` as distinct types), OneOf models that more precisely.

## When to pick ZibStack.NET.Result

Server-side APIs where every handler returns either a success payload or a typed error that needs to flow cleanly to an HTTP response. Built-in `ProblemDetails` conversion, `[CrudApi]` endpoints wired to return `Result<T>` → HTTP status codes automatically. One dependency, no FP ramp-up, works with ASP.NET Core / Minimal API / Controllers without ceremony.

## When to stay on alternatives

- **OneOf** — you genuinely have >2 distinct outcome types and want the compiler to enforce exhaustive matching across all of them.
- **LanguageExt** — your team is comfortable with FP idioms and you want the whole toolbox (Option/Try/Either/Validation/State/...).
- **ErrorOr** — you specifically prefer its `Error` record API and the implicit conversion patterns in its ecosystem.
- **FluentResults** — you need rich error hierarchies with multiple errors per result, metadata on each, and aren't focused on HTTP mapping.

## Sources

- [Functional Error Handling in .NET With the Result Pattern (Milan Jovanović)](https://www.milanjovanovic.tech/blog/functional-error-handling-in-dotnet-with-the-result-pattern)
- [OneOf<> vs FluentResults (Kevin Smith)](https://kevsoft.net/2025/06/20/one-of-vs-fluent-results.html)
- [FluentResults on GitHub](https://github.com/altmann/FluentResults)
- [OneOf on GitHub](https://github.com/mcintyre321/OneOf)
- [ErrorOr on GitHub](https://github.com/amantinband/error-or)
- [LanguageExt on GitHub](https://github.com/louthy/language-ext)
