---
title: ZibStack.NET.Dto — Alternatives
description: "How ZibStack.NET.Dto compares to AutoMapper/Mapster/Mapperly (mapping-only), Sieve (filter-only), OData (heavy query + endpoints), MediatR (pipeline), and VS scaffolding templates — one attribute replaces the combo."
---

"CRUD in .NET" isn't one library — it's usually a **stack**: a mapper (AutoMapper / Mapster / Mapperly), a query DSL (Sieve / OData), a validation lib (FluentValidation), a pipeline (MediatR), a scaffolding template (OnionAPI / VS). ZibStack.NET.Dto collapses the request/response/query/mapping/endpoints surface into compile-time generation from one `[CrudApi]` attribute, while deliberately staying out of the mediator and sink business.

The main table below compares the four most common picks — AutoMapper, Mapperly, Sieve, OData. Additional tools (Mapster, MediatR, VS Scaffolding) are discussed in prose below.

| Feature | **ZibStack.NET.Dto** | AutoMapper | Mapperly | Sieve | OData |
|---|---|---|---|---|---|
| Scope | CRUD DTO + endpoints + query + mapping + JSON Merge Patch | object mapping | object mapping | filter / sort / paging | full query protocol + endpoints |
| Dispatch | **Roslyn source gen (compile time)** | runtime reflection + expression trees | Roslyn source gen | runtime reflection | runtime + query translator |
| Price | ✅ MIT free | **Commercial (2025 change)** | MIT free | MIT free | MIT free |
| DTOs from entity | ✅ auto from `[CrudApi]` (Create / Update / Response / Query variants) | manual `CreateMap<T, TDto>()` | manual partial class | n/a | auto-project |
| JSON Merge Patch / partial update | ✅ `PatchField<T>` tri-state (null / missing / set) | ❌ | ❌ | ❌ | partial via PATCH |
| Filter / sort / paging DSL | ✅ `[QueryDto]` + filter/sort DSL | n/a | n/a | ✅ `Sieve(CanSort=true)` | ✅ heavy OData protocol |
| Minimal API endpoint generation | ✅ `Map{Entity}Endpoints(...)` | ❌ | ❌ | ❌ | ❌ (controller only) |
| `[ApiController]` generation | ✅ both Minimal API and Controller styles | ❌ | ❌ | ❌ | ❌ |
| Sensitive / response-filtering attrs | ✅ `[DtoIgnore(target)]`, `[DtoOnly]`, `[ListIgnore]`, `[QueryIgnore]` | ❌ | ❌ | ❌ | `[Select]` |
| External-type configuration | ✅ `IDtoConfigurator` fluent + `[CreateDtoFor]` / `[UpdateDtoFor]` | `Profile`s | partial class | attribute | EDM builder |
| Runtime reflection | ❌ zero reflection | ✅ heavy | ❌ | ✅ | ✅ |
| Output visibility | ✅ generated `.g.cs` in `obj/` on build | obscure (runtime) | ✅ visible partials | obscure | obscure |
| Regenerates on source change | ✅ on every build, no drift | n/a | ✅ on build | n/a | n/a |

**Other tools worth mentioning:**

- **Mapster** — mapping-only; runtime with optional Mapster.Tool codegen. Active but development stalled in 2025; MIT free. Good pick if you need just mapping and don't want AutoMapper's new commercial license.
- **MediatR** — request/response pipeline with behaviors (validation, logging, caching). **Went commercial in 2025.** Solves a different problem than Dto — cross-cutting pipelines for any request, not just CRUD. Use both if you need pipelines on top of CRUD.
- **VS Scaffolding / OnionAPI templates** — drop code once, then you own it. No regeneration; the moment your entity changes, you maintain the scaffold by hand.

## What you give up

ZibStack.NET.Dto isn't a mediator and doesn't try to be — MediatR-style cross-cutting pipelines (pre/post handlers, notifications, dispatching commands by type) still belong in MediatR or a hand-rolled equivalent. ZibStack.NET.Dto only handles the per-entity CRUD surface.

For mapping, the generated Create/Update/Response converters are **opinionated** — they map by property name, respect `[DtoIgnore]` / `[DtoOnly]` filtering, and support `[Flatten]` for nested-to-flat mappings. Complex many-to-one / recursive mappings that AutoMapper's `ForMember(…)` handles are outside Dto's scope; write a manual mapper on top (as a partial method).

## When to pick ZibStack.NET.Dto

Greenfield project or a fresh CRUD resource where the usual stack (AutoMapper + MediatR + Sieve + hand-written endpoints + VS scaffold) feels like a lot of boilerplate for what boils down to "REST over an entity." One `[CrudApi]` attribute generates every variant DTO, every endpoint, the query DSL, and the mapping layer. AOT-friendly, zero-reflection, visible `.g.cs` output. `PatchField<T>` is the JSON Merge Patch tri-state model that nobody else in this space implements.

## When to stay on alternatives

- **MediatR** — you need the full request/response pipeline with behaviors (validation, logging, caching) dispatched by type across hundreds of commands/queries, not just CRUD.
- **Mapperly** — pure object-to-object mapping, no endpoints, no query — and you want a source-generated mapper without the opinion about what a "CRUD DTO" looks like.
- **AutoMapper** — existing codebase already on it, or you need its complex `ForMember` / `Profile` mappings the built-in generator won't synthesize.
- **OData** — you need the full OData protocol (`$expand`, `$select`, `$orderby`, `$count`) on the wire, with clients that speak it natively.
- **Sieve** — you just want filter/sort/paging on existing endpoints, no DTO generation.

## Sources

- [Functional Error Handling in .NET With the Result Pattern (Milan Jovanović)](https://www.milanjovanovic.tech/blog/functional-error-handling-in-dotnet-with-the-result-pattern)
- [AutoMapper vs Mapster vs Mapperly in .NET 2026 (CodingDroplets)](https://codingdroplets.com/automapper-vs-mapster-vs-mapperly-in-net-which-object-mapper-should-your-team-use-in-2026)
- [AutoMapper and MediatR Going Commercial (OnlyIan)](https://onlyian.com/blog/AutoMapper-and-MediatR-Going-Commercial---What-does-this-mean)
- [Sieve](https://github.com/Biarity/Sieve)
- [ASP.NET Core OData](https://learn.microsoft.com/en-us/odata/webapi-8/fundamentals/query-options)
