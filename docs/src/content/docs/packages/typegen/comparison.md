---
title: ZibStack.NET.TypeGen — Alternatives
description: "How ZibStack.NET.TypeGen compares to NSwag, jburzynski/TypeGen, Tapper, Reinforced.Typings, AutoRest, and OpenAPI Generator — multi-target, compile-time, no running app."
---

"C# → TypeScript" and "C# → OpenAPI" each have their own ecosystem, and most tools stay on one axis. ZibStack.NET.TypeGen is a **Roslyn source generator that emits four targets in parallel from the same C# DTOs** — TypeScript, OpenAPI 3.0, Python (Pydantic), and Zod — at compile time, without reflection, without a running app.

Note: there is a separate project called **TypeGen** ([jburzynski/TypeGen](https://github.com/jburzynski/TypeGen)) — reflection-based single-target C#→TS tool. Our package is `ZibStack.NET.TypeGen` (namespaced) to avoid collision, but keep this in mind when searching.

The main table compares the four closest tools — NSwag (OpenAPI + TS client), Tapper (Roslyn-adjacent C#→TS), jburzynski/TypeGen (reflection-based C#→TS with attributes), and AutoRest (multi-language from OpenAPI). Reinforced.Typings and OpenAPI Generator are discussed in prose below.

| Feature | **ZibStack.NET.TypeGen** | NSwag | Tapper | jburzynski/TypeGen | AutoRest |
|---|---|---|---|---|---|
| Mechanism | **Roslyn source gen (runs in compiler)** | reflection + OpenAPI intermediate | dotnet tool CLI | reflection + CLI / `dotnet tool` | OpenAPI-driven |
| Needs running app / compiled DLL | ❌ pure compile-time | ✅ yes | ❌ | ✅ yes | external spec |
| Regenerates on `dotnet build` | ✅ + live on IDE save | partial (build target) | ❌ manual rerun | ❌ manual rerun | ❌ |
| TypeScript | ✅ | ✅ | ✅ | ✅ | ✅ |
| TS string-literal unions (modern) | ✅ default | partial | ✅ | ❌ (TS enum only) | ❌ |
| OpenAPI 3.0 document | ✅ from same DTOs | ✅ (core feature) | ❌ | ❌ | consumes, doesn't emit |
| OpenAPI `paths:` without a running app | ✅ synthesized from `[CrudApi]` + scanned from `[ApiController]` + Minimal API | ❌ needs host | n/a | n/a | n/a |
| Python (Pydantic v2) | ✅ native | ❌ | ❌ | ❌ | via OpenAPI |
| Zod (runtime TS validation schemas) | ✅ native | ❌ | ❌ | ❌ | ❌ |
| MessagePack | ❌ | ❌ | ✅ | ❌ | ❌ |
| Compile-time fluent configurator | ✅ `ITypeGenConfigurator` | ❌ | ❌ | ❌ | ❌ |
| Per-class / per-property attributes | ✅ `[TsName]`, `[TsType]`, `[UseType<T>]`, `[OpenApiProperty]`, … | partial | ✅ `[TranspilationSource]` | ✅ `[ExportTsClass]` | ❌ |
| Validation → schema constraints | ✅ DataAnnotations + `[Z…]` → OpenAPI `minLength`/`pattern`, Zod `.email()`, Pydantic | ❌ | ❌ | ❌ | from OpenAPI spec |
| Polymorphic types → discriminated union | ✅ TS + OpenAPI + Zod | partial | ❌ | ❌ | from spec |
| Dto-companion synthesis (`CreateXRequest`, etc.) | ✅ `[CrudApi]` integration | ❌ | ❌ | ❌ | n/a |
| License | MIT | LGPL (with MIT portions) | MIT | MIT | MIT |

**Other tools worth mentioning:**

- **Reinforced.Typings** — reflection + MSBuild, attribute-based TS generation. Older approach; closer to jburzynski/TypeGen in design. Works on .NET Framework too.
- **OpenAPI Generator** (Apache 2.0) — the Swiss Army knife for generating clients from OpenAPI specs in 30+ languages. Pair it with our OpenAPI output if you want multi-language HTTP clients: `ZibStack.NET.TypeGen` → `openapi.yaml` → `openapi-generator` → Java/Ruby/PHP/…

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
