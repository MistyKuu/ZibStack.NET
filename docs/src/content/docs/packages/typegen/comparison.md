---
title: ZibStack.NET.TypeGen — Alternatives
description: "How ZibStack.NET.TypeGen compares to NSwag, jburzynski/TypeGen, Tapper, Reinforced.Typings, AutoRest, and OpenAPI Generator — multi-target, compile-time, no running app."
---

"C# → TypeScript" and "C# → OpenAPI" each have their own ecosystem, and most tools stay on one axis. ZibStack.NET.TypeGen is a **Roslyn source generator that emits four targets in parallel from the same C# DTOs** — TypeScript, OpenAPI 3.0, Python (Pydantic), and Zod — at compile time, without reflection, without a running app.

Note: there is a separate project called **TypeGen** ([jburzynski/TypeGen](https://github.com/jburzynski/TypeGen)) — reflection-based single-target C#→TS tool. Our package is `ZibStack.NET.TypeGen` (namespaced) to avoid collision, but keep this in mind when searching.

| Feature | NSwag | jburzynski/TypeGen | Tapper | Reinforced.Typings | AutoRest | OpenAPI Generator | **ZibStack.NET.TypeGen** |
|---|---|---|---|---|---|---|---|
| Mechanism | reflection + OpenAPI intermediate | reflection + CLI / `dotnet tool` | dotnet tool CLI | reflection + MSBuild | OpenAPI-driven | OpenAPI-driven | **Roslyn source gen (runs in compiler)** |
| Needs running app / compiled DLL | ✅ yes | ✅ yes | ❌ | ✅ compiled DLL | external spec | external spec | ❌ pure compile-time |
| Regenerates on `dotnet build` | partial (build target) | ❌ manual rerun | ❌ manual rerun | ✅ | ❌ | ❌ | ✅ + live on IDE save |
| TypeScript | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| TS string-literal unions (modern) | partial | ❌ (TS enum only) | ✅ | partial | ❌ | ❌ | ✅ default |
| OpenAPI 3.0 document | ✅ (core feature) | ❌ | ❌ | ❌ | consumes, doesn't emit | consumes | ✅ from same DTOs |
| OpenAPI `paths:` without a running app | ❌ needs host | n/a | n/a | n/a | n/a | n/a | ✅ synthesized from `[CrudApi]` + scanned from `[ApiController]` + Minimal API |
| Python (Pydantic v2) | ❌ | ❌ | ❌ | ❌ | via OpenAPI | via OpenAPI | ✅ native |
| Zod (runtime TS validation schemas) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ native |
| MessagePack | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Compile-time fluent configurator (Roslyn-parsed) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ `ITypeGenConfigurator` |
| Per-class / per-property override attributes | partial | ✅ `[ExportTsClass]` | ✅ `[TranspilationSource]` | ✅ | ❌ | ❌ | ✅ `[TsName]`, `[TsType]`, `[UseType<T>]`, `[OpenApiProperty]`, … |
| Validation → schema constraints | ❌ | ❌ | ❌ | ❌ | from OpenAPI spec | from OpenAPI spec | ✅ DataAnnotations + `[Z…]` → OpenAPI `minLength`/`pattern`, Zod `.email()`, Pydantic |
| Polymorphic types (`[JsonPolymorphic]`) → discriminated union | partial | ❌ | ❌ | ❌ | from spec | from spec | ✅ TS + OpenAPI + Zod |
| Dto-companion synthesis (`CreateXRequest`, etc.) | ❌ | ❌ | ❌ | ❌ | n/a | n/a | ✅ `[CrudApi]` integration |
| License | LGPL (with MIT portions) | MIT | MIT | MIT | MIT | Apache 2.0 | MIT |

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
