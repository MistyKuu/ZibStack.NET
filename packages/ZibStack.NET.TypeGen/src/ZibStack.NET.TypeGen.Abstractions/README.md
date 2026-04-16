# ZibStack.NET.TypeGen.Abstractions

Attributes, settings types, and the `ITypeGenConfigurator` interface consumed by
[ZibStack.NET.TypeGen](https://www.nuget.org/packages/ZibStack.NET.TypeGen) — the
Roslyn source generator that emits TypeScript and OpenAPI from C# DTOs.

## What's in this package

- **`[GenerateTypes]`** — entry-point attribute. Declares which targets (TS /
  OpenAPI / Python) and output directory a DTO should emit to.
- **Per-class overrides** — `[TsName]`, `[OpenApiSchemaName]`, `[TsIgnore]`,
  `[OpenApiIgnore]`.
- **Per-property overrides** — `[TsType("…")]` / `[TsType<T>]`,
  `[OpenApiProperty(...)]`, `[TsIgnore]`, `[OpenApiIgnore]`.
- **`ITypeGenConfigurator` + `ITypeGenBuilder` + `IPropertyBuilder<T, TProp>`** —
  fluent DSL for project-wide defaults and per-type / per-property
  configuration without touching model files.
- **Settings records** — `TypeScriptSettings`, `OpenApiSettings`, `PythonSettings`,
  plus `NameStyle`, `TypeScriptFileLayout`, `PythonStyle` enums.

## Why separate from the analyzer package

Roslyn analyzers run in an isolated load context that doesn't auto-resolve
dependency assemblies. Keeping the attribute + settings surface in its own
package lets the analyzer reference this assembly at consume time without the
generator binary itself carrying it — user code depends on the lightweight
abstractions, the generator works against the mirror types internally.

You don't normally install this package directly — `ZibStack.NET.TypeGen` pulls
it in transitively.

## Docs

[mistykuu.github.io/ZibStack.NET/packages/typegen](https://mistykuu.github.io/ZibStack.NET/packages/typegen/)

## License

MIT.
