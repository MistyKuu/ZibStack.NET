---
title: TypeGen — Configuration & output
description: "Project-wide ITypeGenConfigurator fluent DSL, open-generic targeting, per-companion overrides, precedence rules, and the on-disk write pipeline (live generator writes + MSBuild fallback)."
---

## Layers (lowest → highest precedence)

1. Defaults
2. Global `TypeScript` / `OpenApi` / `Python` / `Zod` blocks in `ITypeGenConfigurator`
3. `ForType<T>()` per-type fluent overrides
4. Class / property attributes (`[TsName]`, `[OpenApiProperty]`, etc.)

## Project-wide — `ITypeGenConfigurator` (fluent DSL)

One class per project, picked up automatically by the generator. The `Configure`
method body is a fluent DSL parsed at compile time — it's never actually invoked
at runtime, so all arguments must be literal expressions (string literals, enum
members, constants). Anything dynamic is invisible and surfaces as diagnostic
`TG0013`.

```csharp
public sealed class TypeGenConfig : ITypeGenConfigurator
{
    public void Configure(ITypeGenBuilder b)
    {
        b.TypeScript(ts =>
        {
            ts.OutputDir = "../client/src/api";
            ts.FileLayout = TypeScriptFileLayout.SingleFile;
            ts.SingleFileName = "models.ts";
            ts.PropertyNameStyle = NameStyle.CamelCase;
        });

        b.OpenApi(oa =>
        {
            oa.OutputPath = "../api/openapi.yaml";
            oa.Title = "Order Service";
            oa.Version = "2.1.0";
            oa.Description = "Public API for the order service.";
        });

        // Per-type overrides for DTOs you can't (or don't want to) annotate —
        // e.g. types from a referenced library.
        b.ForType<Order>()
            .TsName("OrderDto")
            .OutputDir("generated/orders");

        b.ForType<InternalAudit>().Ignore();

        // Fluent-only discovery — no [GenerateTypes] needed on the class.
        // .WithGeneratedTypes(targets) opts the type into emission for the listed
        // targets. Combine with the usual chain (TsName, .Property, etc.) to
        // tweak the output.
        b.ForType<Article>()
            .WithGeneratedTypes(TypeTarget.TypeScript | TypeTarget.OpenApi)
            .TsName("ArticleDto")
            .Property(p => p.Body).TsType("string | null");
    }
}
```

> **Discovery vs override.** Without `.WithGeneratedTypes(...)`, the fluent block
> is a no-op for types that don't carry `[GenerateTypes]` — the chain just sits
> there registering overrides for a class TypeGen never sees. Adding the marker
> method is the explicit "yes, emit code for this type" signal.

## Targeting generic types — `ForType(typeof(...))`

Open generics can't be passed as a C# type argument (`ForType<Base<>>()` is a
syntax error), so pair the second `ForType` overload with `typeof(...)`:

```csharp
// Open form — the canonical way to target every Base<T> instantiation at once.
b.ForType(typeof(Base<>))
    .Property("InternalTrace").TsIgnore()
    .Property("DebugToken").OpenApiIgnore();

// Closed form works too — both collapse onto the same Base<T> key that the
// schema model uses, so a single line covers Base<int>, Base<string>, etc.
b.ForType(typeof(Base<int>))
    .TsName("BaseDto");
```

Strongly-typed lambda selectors aren't available on this path (the type
argument is erased) — use the string-based `Property(name)` overload. It's
parsed as a literal; `nameof(T.Member)` also works since it's a compile-time
constant.

```csharp
b.ForType<Order>().Property(nameof(Order.Email)).TsName("emailAddress");
```

> **Prefer a closed form for typed selectors.** When you want refactor-safe
> `c => c.Property` lambdas on a generic, use `b.ForType<Base<SomeT>>()` with
> **any** valid closed instantiation — the parser normalizes to the open
> `Base<T>` key anyway, so one override covers every construction. Reach for
> `ForType(typeof(Base<>))` only when no closed form satisfies `Base<T>`'s
> type constraints (rare).

## Targeting Dto-generated companion DTOs

When the parent type carries `[CrudApi]` (or `[CreateDto]`/`[UpdateDto]`/
`[ResponseDto]`), Dto generates `Create{X}Request` / `Update{X}Request` /
`{X}Response` companion records. Roslyn's source-generator architecture
prevents TypeGen from resolving those records as symbols in the same
compilation pass — so you can't write `b.ForType<CreateArticleRequest>()`
and expect the symbol to bind.

TypeGen handles this with a synthesis path: when the fluent type argument
matches the `Create{X}Request` / `Update{X}Request` / `{X}Response` naming
pattern AND the symbol is unresolvable, TypeGen looks for the parent type
`X` in the user's assembly and emits a synthetic schema from its properties.

```csharp
// Standalone — no [GenerateTypes] on Article needed, no anchor needed either.
// Generates ONLY the Create variant as TS + OpenAPI; Update/Response are NOT
// emitted because they aren't listed.
b.ForType<CreateArticleRequest>()
    .WithGeneratedTypes(TypeTarget.TypeScript | TypeTarget.OpenApi)
    .TsName("ArticleDto");
```

Per-companion fluent overrides (`TsName`, `OpenApiName`) apply on the
synthesized schema. The attribute path (`[CrudApi]` on parent, no fluent)
still emits all three companions automatically — fluent is for chirurgical
opt-in to a subset.

> Only one `ITypeGenConfigurator` per project — multiple implementations fire
> diagnostic `TG0010`. Unrecognized fluent calls fire `TG0012`.

## Per-class / per-property attributes

Wins over project-wide settings and per-type fluent overrides. Precedence from
lowest to highest: defaults → `TypeScript`/`OpenApi` global blocks →
`ForType<T>()` per-type fluent → class/property attributes.

## Output mechanism

TypeGen writes generated files to disk through two complementary paths:

1. **Live writes from the generator** (default, on every IDE save). The source
   generator itself calls `File.WriteAllText` for each emitted file. Since
   Roslyn re-runs the generator on each compile cycle the IDE triggers, your
   `.ts` / `.yaml` / `.py` files refresh as soon as you save the source.
   Wrapped in `try/catch` — sandboxed analyzer hosts (some Rider configs,
   restricted CI containers) silently fall back to path #2.
2. **MSBuild post-build target** (shipped in `build/ZibStack.NET.TypeGen.targets`,
   auto-imported by the NuGet package). Reads a manifest `.g.cs` the
   generator emits to `obj/generated/` and writes the same set of files
   via an inline `RoslynCodeTaskFactory` C# task. Always runs on
   `dotnet build`, regardless of whether path #1 succeeded.

Both paths skip writes when content is byte-identical (mtime stays stable —
keeps file-watcher-driven dev servers like Vite from looping). Both sweep
stale files in the touched directories: any file carrying our `@generated`
banner that isn't in the current emit set gets deleted, so renames don't
leak orphaned outputs.

No companion task DLL, no reflection over the built assembly — the inline task is ~60
lines of C# directly in the `.targets` file. Files are skipped when their content is
unchanged (stable mtimes, no file-watcher thrash in frontend dev servers).
