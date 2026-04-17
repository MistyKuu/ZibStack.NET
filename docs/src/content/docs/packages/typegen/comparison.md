---
title: ZibStack.NET.TypeGen — Alternatives
description: "How ZibStack.NET.TypeGen compares to NSwag, jburzynski/TypeGen, Tapper, Reinforced.Typings, AutoRest, and OpenAPI Generator — multi-target, compile-time, no running app."
---

"C# → TypeScript" and "C# → OpenAPI" each have their own ecosystem, and most tools stay on one axis. ZibStack.NET.TypeGen is a **Roslyn source generator that emits four targets in parallel from the same C# DTOs** — TypeScript, OpenAPI 3.0, Python (Pydantic), and Zod — at compile time, without reflection, without a running app.

Note: there is a separate project called **TypeGen** ([jburzynski/TypeGen](https://github.com/jburzynski/TypeGen)) — reflection-based single-target C#→TS tool. Our package is `ZibStack.NET.TypeGen` (namespaced) to avoid collision, but keep this in mind when searching.

The main table compares the five most relevant tools — Microsoft.AspNetCore.OpenApi (.NET 9/10 built-in), NSwag, Tapper, jburzynski/TypeGen, and AutoRest. Others (Reinforced.Typings, OpenAPI Generator) are in prose below.

| Feature | **ZibStack.NET.TypeGen** | Microsoft.AspNetCore.OpenApi | NSwag | Tapper | jburzynski/TypeGen |
|---|---|---|---|---|---|
| Mechanism | **Roslyn source gen (runs in compiler)** | MSBuild launches app with mock server post-build | reflection + OpenAPI intermediate | dotnet tool CLI | reflection + CLI |
| Needs running app / compiled DLL | ❌ pure compile-time | ✅ boots app with mock server | ✅ yes | ❌ | ✅ yes |
| Works without DB / secrets / DI | ✅ | ❌ build fails if DI can't resolve | ❌ | ✅ | ❌ |
| Regenerates on `dotnet build` | ✅ + live on IDE save | ✅ (post-build target) | partial | ❌ manual rerun | ❌ manual rerun |
| TypeScript | ✅ | ❌ | ✅ | ✅ | ✅ |
| Python (Pydantic v2) | ✅ native | ❌ | ❌ | ❌ | ❌ |
| Zod (runtime TS validation) | ✅ native | ❌ | ❌ | ❌ | ❌ |
| OpenAPI document | ✅ 3.0 from DTOs | ✅ 3.1 from runtime | ✅ (core feature) | ❌ | ❌ |
| OpenAPI `paths:` fidelity | static: `[CrudApi]` + `[ApiController]` + Minimal API syntax scan | **full runtime truth** (middleware, auth, endpoint filters, `Produces<T>()`) | runtime host | n/a | n/a |
| Swagger UI / Scalar OOTB | ❌ (feed yaml to Scalar manually) | ✅ native | ✅ | n/a | n/a |
| TS string-literal unions | ✅ default | n/a | partial | ✅ | ❌ |
| MessagePack | ❌ | ❌ | ❌ | ✅ | ❌ |
| Fluent configurator | ✅ `ITypeGenConfigurator` | transformers API | ❌ | ❌ | ❌ |
| Validation → schema constraints | ✅ DataAnnotations → OpenAPI `minLength`/`pattern`, Zod `.email()`, Pydantic | ✅ via runtime model binding | ❌ | ❌ | ❌ |
| Polymorphic types → discriminated union | ✅ TS + OpenAPI + Zod | ✅ via `[JsonPolymorphic]` runtime | partial | ❌ | ❌ |
| Dto-companion synthesis | ✅ `[CrudApi]` integration | n/a | ❌ | ❌ | ❌ |
| License | MIT | free (part of ASP.NET Core) | LGPL | MIT | MIT |

**Other tools worth mentioning:**

- **AutoRest** (MIT) — multi-language client generation from an OpenAPI spec. Consumes specs, doesn't emit them. Pair with our OpenAPI output or Microsoft's.
- **Reinforced.Typings** — reflection + MSBuild, attribute-based TS generation. Older approach; works on .NET Framework too.
- **OpenAPI Generator** (Apache 2.0) — the Swiss Army knife for generating clients from OpenAPI specs in 30+ languages. Pair it with our OpenAPI output: `ZibStack.NET.TypeGen` → `openapi.yaml` → `openapi-generator` → Java/Ruby/PHP/…

### ZibStack.NET.TypeGen + Microsoft.AspNetCore.OpenApi — complementary, not competing

The two tools address **different layers**:

- **Microsoft's package** sees the **runtime truth** — every middleware, auth policy, endpoint filter, `Produces<T>()` call, and model binding quirk. It's the most faithful representation of what your API actually does. But it needs to boot the app, so CI environments without a database or external services can't use it.
- **TypeGen** sees the **source truth** — the C# DTOs and their attributes, without booting anything. It can't see runtime middleware, but it also can't break when the DB is unavailable. And it emits TS + Python + Zod alongside the OpenAPI, which Microsoft's package doesn't do.

**Best-of-both setup:** use TypeGen for multi-target contract generation (TS / Python / Zod) and a compile-time OpenAPI draft during development. Use Microsoft's package for the production-served OpenAPI document with full middleware fidelity. The two don't conflict — they emit to different paths and serve different consumers.

## What you give up

MessagePack output (Tapper's differentiator) — if your transport is MsgPack, Tapper is the right tool. No plan to add MsgPack here.

"One-click client-library generation" of HTTP clients (AutoRest / OpenAPI Generator's flagship feature) — ZibStack.NET.TypeGen emits the **contract**; write your fetch/axios/msw wrapper by hand or feed the generated `openapi.yaml` into OpenAPI Generator for client code. That's a deliberate split: ZibStack.NET.TypeGen owns the single source of truth (your C# DTOs), OpenAPI Generator owns the 30+ language HTTP client templates.

## When to pick ZibStack.NET.TypeGen

You want the frontend TS interface, the OpenAPI document (for Redoc / Scalar / Swagger UI or handing off to another team's code generator), the Python contract for a FastAPI back-office, and the Zod runtime validator — **all from the same C# DTOs, regenerated on every `dotnet build`**, without running the server, without reflection, without a template file. The OpenAPI `paths:` synthesis means `[CrudApi]` classes and hand-written `[ApiController]` / Minimal API endpoints all contribute to one unified document.

## When to stay on alternatives

- **NSwag** — you need the full Swashbuckle-style OpenAPI document with a running app, or the TypeScript HTTP client codegen.
- **Tapper** — you specifically need MessagePack serialization support, or you're TS-only and prefer a small CLI tool over a source generator.
- **AutoRest / OpenAPI Generator** — you already have an OpenAPI spec (from another team / a contract-first workflow) and need client libraries in multiple languages.
- **jburzynski/TypeGen / Reinforced.Typings** — you're on older .NET without C# 12 interceptor support, and reflection-based TS-only generation suits your workflow.

## Sources

- [NSwag on GitHub](https://github.com/RicoSuter/NSwag)
- [jburzynski/TypeGen on GitHub](https://github.com/jburzynski/TypeGen)
- [nenoNaninu/Tapper on GitHub](https://github.com/nenoNaninu/Tapper)
- [Reinforced.Typings on GitHub](https://github.com/reinforced/Reinforced.Typings)
- [Alex Klaus — 6+ ways to marry C# with TypeScript](https://alex-klaus.com/marry-csharp-typescript/)
- [AutoRest on GitHub](https://github.com/Azure/autorest)
- [OpenAPI Generator](https://openapi-generator.tech/)
