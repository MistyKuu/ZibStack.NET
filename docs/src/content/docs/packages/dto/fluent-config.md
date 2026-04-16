---
title: Fluent IDtoConfigurator
description: Project-wide fluent DSL alternative to attribute markers — central typed builder, mixable per-class with attributes.
---

## Fluent configuration (`IDtoConfigurator`)

Attributes are great for locality (you read a model file top-to-bottom and see
exactly what each property does in each DTO). But they don't fit every case:

- You want to configure a DTO for a type **you can't annotate** (third-party library, generated code).
- You want all DTO settings for the project in one **central place** for review.
- You want to **override** a setting from a model without editing it (e.g. tighten a
  CRUD endpoint's operation set in production).

`IDtoConfigurator` is the fluent alternative — implement once per project, the
generator parses the `Configure` method body at compile time. Both the fluent
DSL and attribute markers work; **mix and match per class as you prefer**.

```csharp
internal sealed class DtoConfig : IDtoConfigurator
{
    public void Configure(IDtoBuilder b)
    {
        // Pure fluent — Article has zero Dto attributes on the model.
        b.ForType<Article>()
            .CreateDto(opts => opts.Name = "ArticleCreate")
            .UpdateDto()
            .ResponseDto()
            .QueryDto(q =>
            {
                q.DefaultSort = "PublishedAt";
                q.DefaultSortDirection = SortDirection.Desc;
            })
            .Property(p => p.Id).IgnoreIn(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)
            .Property(p => p.Body).RenameTo("content")
            .Property(p => p.PublishedAt).IgnoreIn(DtoTarget.Create);

        // Mixed mode — Player keeps its [CrudApi] attribute marker as the
        // discoverable signal "this is a CRUD endpoint", but pulls options +
        // per-property overrides from here.
        b.ForType<Player>()
            .CrudApi(api =>
            {
                api.Operations = CrudOperations.Create | CrudOperations.Update | CrudOperations.GetById;
                api.Route = "api/v2/players";
            })
            .Property(p => p.Password).Ignore();
    }
}
```

### What's parseable

The generator reads **method-call syntax**, not runtime invocations. Arguments
must be:
- String / numeric / bool literals
- Enum members (`DtoTarget.Create`, `SortDirection.Desc`)
- Bitwise OR of enum members (`DtoTarget.Create | DtoTarget.Update`)
- `typeof(...)` for `Validator` properties

Anything dynamic (locals, method calls, ternaries) is invisible. Property selectors
must be a single-member access on the lambda parameter (`p => p.X` — no `p.X.Y` or
method calls).

### Type-level methods

| Method | Equivalent attribute |
|---|---|
| `b.ForType<T>().CreateDto(opts => ...)` | `[CreateDto(Name=, Validator=)]` |
| `b.ForType<T>().UpdateDto(opts => ...)` | `[UpdateDto(Name=, Validator=)]` |
| `b.ForType<T>().CreateOrUpdateDto(opts => ...)` | `[CreateOrUpdateDto(Name=, CreateValidator=, UpdateValidator=)]` |
| `b.ForType<T>().ResponseDto(opts => ...)` | `[ResponseDto(Name=)]` |
| `b.ForType<T>().QueryDto(opts => ...)` | `[QueryDto(Name=, Sortable=, DefaultSort=, DefaultSortDirection=)]` |
| `b.ForType<T>().CrudApi(api => ...)` | overrides settings on the existing `[CrudApi]` attribute (the marker stays on the class) |

### Property-level methods

| Method | Equivalent attribute |
|---|---|
| `.Property(p => p.X).Ignore()` | `[DtoIgnore]` |
| `.Property(p => p.X).IgnoreIn(targets)` | `[DtoIgnore(targets)]` |
| `.Property(p => p.X).OnlyIn(targets)` | `[DtoOnly(targets)]` |
| `.Property(p => p.X).RenameTo("y")` | `[DtoName("y")]` |

Property-level fluent overrides apply to **every** generated DTO variant (Create,
Update, Response, Query — except `RenameTo` on Query, see limitations). Stack
multiple `.Property(...)` calls in the same chain — they all attach to the same
`ForType<T>` block.

### Mixing fluent with attributes

The fluent layer is **additive**. Concrete behavior when both apply:

- **Attribute marker present, no fluent block** → attributes win, no surprise.
- **Fluent marker, no attributes on class** → fluent fully drives that type.
- **Both** → variants the fluent enables get generated (in addition to attribute
  ones); fluent per-property overrides override attribute-derived values; fluent
  CrudApi options override attribute settings (`Operations`, `Route`,
  `KeyProperty`, policies). The attribute marker's *presence* is what triggers
  emission of [CrudApi]-implied DTOs.

### Limitations

- **`.RenameTo` on Query DTOs** is a no-op — the Query generator uses the
  property name inside expression trees that access the entity
  (`x => x.PropertyName`), so renaming would break EF compilation. Use
  `[DtoName]` on the property when you need a JSON-name override on a Query DTO.
- **`.Flatten()`** is exposed on `IDtoPropertyBuilder` for symmetry with
  `[Flatten]` but the recursive flatten machinery still reads the attribute,
  not the fluent flag — set `[Flatten]` on the property if you need it today.
- Per-property fluent overrides don't apply inside `[CreateDtoFor]` /
  `[UpdateDtoFor]` partial records — those use the attribute's own
  `Ignore = new[] { ... }` array.


### `required` keyword

Properties marked `required` are validated as mandatory in create validation. In update validation, all properties are optional.

### Custom names

```csharp
[CreateDto(Name = "NewPlayerDto")]
[UpdateDto(Name = "EditPlayerDto")]
public class Player { ... }

// Combined
[CreateOrUpdateDto(Name = "PlayerDto")]
public class Player { ... }
```

### Create/Update-only properties

```csharp
[CreateDto]
[UpdateDto]
public class Player
{
    public required string Name { get; set; }

    [DtoOnly(DtoTarget.Create)]
    public required string Password { get; set; }    // only in CreatePlayerRequest

    [DtoOnly(DtoTarget.Update)]
    public string? DeactivationReason { get; set; }  // only in UpdatePlayerRequest
}
```

With `[CreateOrUpdateDto]`, `[DtoOnly(DtoTarget.Create)]` properties are included but excluded from `ValidateForUpdate()` and `ApplyTo()`. `[DtoOnly(DtoTarget.Update)]` properties are excluded from `ValidateForCreate()` and `ToEntity()`.

