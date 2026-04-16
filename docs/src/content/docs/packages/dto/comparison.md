---
title: ZibStack.NET.Dto — Alternatives
description: "How ZibStack.NET.Dto compares to AutoMapper/Mapster/Mapperly (mapping-only), Sieve (filter-only), OData (heavy query + endpoints), MediatR (pipeline), and VS scaffolding templates — one attribute replaces the combo."
---

"CRUD in .NET" isn't one library — it's usually a **stack**: a mapper (AutoMapper / Mapster / Mapperly), a query DSL (Sieve / OData), a validation lib (FluentValidation), a pipeline (MediatR), a scaffolding template (OnionAPI / VS). ZibStack.NET.Dto collapses the request/response/query/mapping/endpoints surface into compile-time generation from one `[CrudApi]` attribute, while deliberately staying out of the mediator and sink business.

| Feature | AutoMapper | Mapster | Mapperly | Sieve | OData | MediatR | VS Scaffolding | **ZibStack.NET.Dto** |
|---|---|---|---|---|---|---|---|---|
| Scope | object mapping | object mapping | object mapping | filter / sort / paging | full query protocol + endpoints | request pipeline | one-time scaffold | CRUD DTO + endpoints + query + mapping + JSON Merge Patch |
| Dispatch | runtime reflection + expression trees | runtime + optional codegen (Mapster.Tool) | Roslyn source gen | runtime reflection | runtime + query translator | runtime + DI | none (drops code) | **Roslyn source gen (compile time)** |
| Price | **Commercial (2025 change)** | MIT free | MIT free | MIT free | MIT free | **Commercial (2025 change)** | free | ✅ MIT free |
| DTOs from entity | manual `CreateMap<T, TDto>()` | manual / attributes | manual partial class | n/a | auto-project | n/a | one-shot template | ✅ auto from `[CrudApi]` (Create / Update / Response / Query variants) |
| JSON Merge Patch / partial update | ❌ | ❌ | ❌ | ❌ | partial via PATCH verb | ❌ | ❌ | ✅ `PatchField<T>` tri-state (null / missing / set) |
| Filter / sort / paging DSL | n/a | n/a | n/a | ✅ `Sieve(CanSort=true)` | ✅ heavy OData protocol | n/a | n/a | ✅ `[QueryDto]` + filter/sort DSL |
| Minimal API endpoint generation | ❌ | ❌ | ❌ | ❌ | ❌ (controller only) | ❌ | one-shot | ✅ `Map{Entity}Endpoints(...)` |
| `[ApiController]` generation | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | one-shot | ✅ both Minimal API and Controller styles |
| Sensitive / response-filtering attrs | ❌ | ❌ | ❌ | ❌ | `[Select]` | ❌ | ❌ | ✅ `[DtoIgnore(target)]`, `[DtoOnly]`, `[ListIgnore]`, `[QueryIgnore]` |
| External-type configuration | `Profile`s | fluent `TypeAdapterConfig` | partial class | attribute | EDM builder | n/a | n/a | ✅ `IDtoConfigurator` fluent + `[CreateDtoFor]` / `[UpdateDtoFor]` |
| Runtime reflection | ✅ heavy | optional | ❌ | ✅ | ✅ | ✅ | n/a | ❌ zero reflection |
| Output visibility | obscure (runtime) | opt-in file | ✅ visible partials | obscure | obscure | n/a | ✅ scaffolded once then yours to edit | ✅ generated `.g.cs` in `obj/` on build |
| Regenerates on source change | n/a | on `mapster gen` | ✅ on build | n/a | n/a | n/a | ❌ manual rerun | ✅ on every build, no drift |

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
