---
title: TypeGen — Diagnostic reference
description: "Every TG00xx diagnostic emitted by the TypeGen analyzer, its severity, and the condition that fires it."
---

| ID | Severity | Trigger |
|---|---|---|
| `TG0001` | Warning | `[GenerateTypes]` with `Targets = TypeTarget.None` — nothing to emit |
| `TG0002` | Warning | Property type the emitter cannot translate — add `[TsType]` / `[OpenApiProperty]` or `[TsIgnore]` / `[OpenApiIgnore]` |
| `TG0003` | Error | Generic type on `[GenerateTypes]` — not supported in the MVP |
| `TG0010` | Error | More than one `ITypeGenConfigurator` in this project |
| `TG0011` | Error | Empty `OutputDir` on `[GenerateTypes]` |
| `TG0012` | Warning | Unrecognized fluent call in `ITypeGenConfigurator.Configure` |
| `TG0013` | Warning | Non-literal argument passed to a configurator DSL method |
| `TG0014` | Warning | `[CrudApi]` on class without `[GenerateTypes]` — `paths:` won't include it |
| `TG0020` | Info | Live regen on save was sandboxed by the IDE analyzer host — output will be written at the next `dotnet build` (use `dotnet watch build` for save-time refresh) |
